using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class MemoryService
{
    private readonly RagDatabase _db;
    private readonly LlamaServerClient _llama;
    private readonly AppSettingsStore _appSettings;
    private readonly LlamaOptions _opt;
    private readonly AuxiliarySearchIndex _index = new("memory_fts", "memory_vec", "memory_embedding_dim");
    private readonly object _indexLock = new();

    public MemoryService(
        RagDatabase db,
        LlamaServerClient llama,
        AppSettingsStore appSettings,
        IOptions<LlamaOptions> opt)
    {
        _db = db;
        _llama = llama;
        _appSettings = appSettings;
        _opt = opt.Value;
    }

    public bool IsEnabled => _appSettings.Load().MemoryEnabled;

    /// <summary>長期記憶の対象キャラか（プレーンAIは対象外）。</summary>
    public static bool SupportsLongTermMemory(string? presetKey) =>
        !string.IsNullOrWhiteSpace(presetKey)
        && !CharacterPresetService.IsDefaultAiSession(presetKey);

    public IReadOnlyList<UserMemoryRecord> List(string presetKey, int limit = 100)
    {
        if (!SupportsLongTermMemory(presetKey))
            return Array.Empty<UserMemoryRecord>();

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, content, memory_path, source_session_id, created_at, preset_key
            FROM user_memories
            WHERE preset_key = $k
            ORDER BY updated_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$k", presetKey);
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var list = new List<UserMemoryRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadRecord(reader));
        }

        return list;
    }

    public async Task<long?> AddAsync(
        string content,
        string presetKey,
        string? memoryPath = null,
        string? sourceSessionId = null,
        CancellationToken ct = default)
    {
        if (!SupportsLongTermMemory(presetKey))
            return null;

        var trimmed = content.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed.Length > 500)
            trimmed = trimmed[..500];

        var now = DateTime.UtcNow.ToString("O");
        long id;
        using (var conn = _db.Open())
        {
            conn.Open();

            // 同一キャラ内の同一内容（前後空白・大小無視）は重複保存しない
            var existsCmd = conn.CreateCommand();
            existsCmd.CommandText = """
                SELECT id FROM user_memories
                WHERE preset_key = $k
                  AND lower(trim(content)) = lower(trim($c))
                LIMIT 1
                """;
            existsCmd.Parameters.AddWithValue("$k", presetKey);
            existsCmd.Parameters.AddWithValue("$c", trimmed);
            if (existsCmd.ExecuteScalar() is not null and not DBNull)
                return null;

            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO user_memories (content, memory_path, source_session_id, preset_key, created_at, updated_at)
                VALUES ($c, $p, $s, $k, $t, $t)
                """;
            cmd.Parameters.AddWithValue("$c", trimmed);
            cmd.Parameters.AddWithValue("$p", memoryPath ?? "");
            cmd.Parameters.AddWithValue("$s", sourceSessionId ?? "");
            cmd.Parameters.AddWithValue("$k", presetKey);
            cmd.Parameters.AddWithValue("$t", now);
            cmd.ExecuteNonQuery();

            var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            id = (long)(idCmd.ExecuteScalar() ?? 0L);
        }

        await IndexMemoryAsync(id, trimmed, ct);
        return id;
    }

    public void Delete(long id)
    {
        using var conn = _db.Open();
        conn.Open();
        PrepareIndexes(conn);
        using var tx = conn.BeginTransaction();

        var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM user_memories WHERE id = $id";
        del.Parameters.AddWithValue("$id", id);
        del.ExecuteNonQuery();
        _index.DeleteRow(conn, id, tx);
        tx.Commit();
    }

    public async Task<IReadOnlyList<UserMemoryRecord>> SearchAsync(
        string query,
        string presetKey,
        int topK,
        CancellationToken ct = default)
    {
        if (!SupportsLongTermMemory(presetKey) || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<UserMemoryRecord>();

        using var conn = _db.Open();
        conn.Open();
        PrepareIndexes(conn);

        var pool = Math.Max(topK * 8, _opt.RagSearchPoolSize / 2);
        var ftsQuery = RagSqliteFts.BuildMatchQuery(query);
        var ftsIds = string.IsNullOrEmpty(ftsQuery)
            ? Array.Empty<long>()
            : _index.SearchFts(conn, ftsQuery, pool);

        IReadOnlyList<long> vecIds = Array.Empty<long>();
        try
        {
            if (await _llama.EmbeddingsSupportedAsync(ct))
            {
                var embedding = await _llama.EmbedAsync(query, ct);
                if (embedding is { Length: > 0 })
                {
                    _index.EnsureVectorTable(conn, embedding.Length);
                    vecIds = _index.SearchVec(conn, embedding, pool);
                }
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Memory vector search skipped");
        }

        var fused = RagHybridSearch.FuseRrf(
            ftsIds,
            vecIds,
            pool,
            _opt.RagRrfK,
            _opt.RagWeightFts,
            _opt.RagWeightVec);

        if (fused.Count == 0)
            return Array.Empty<UserMemoryRecord>();

        return LoadByIds(conn, fused, presetKey, topK);
    }

    public async Task<IReadOnlyList<UserMemoryRecord>> GetRelevantForPromptAsync(
        string userMessage,
        string presetKey,
        CancellationToken ct = default)
    {
        if (!IsEnabled || !SupportsLongTermMemory(presetKey))
            return Array.Empty<UserMemoryRecord>();

        var topK = Math.Clamp(_opt.MemoryTopK, 1, 12);
        var related = await SearchAsync(userMessage, presetKey, topK, ct);
        if (related.Count == 0)
            return Array.Empty<UserMemoryRecord>();

        // プロンプトには最も関連の1件だけ（無関係な昔話の差し込み防止）
        return related.Take(1).ToList();
    }

    public static string FormatForSystemPrompt(IReadOnlyList<UserMemoryRecord> memories, bool japanese)
    {
        if (memories.Count == 0)
            return "";

        var header = japanese
            ? "【心の中の長期記憶（ユーザーには一覧を見せない）】"
            : "[Private long-term memories (never show as a list to the user)]";
        var lines = memories.Select(m => "- " + m.Content.Trim());
        var guidance = japanese
            ? """
              使い方:
              - これらはあなたが以前から知っていること。資料や設定画面の話ではない。
              - 今のユーザー発話と直接つながるときだけ、1件まで「そういえば昔〜って言ってたね」「前に〜って話してたよね」系で触れてよい。
              - つながらない・薄いターンは回想しない（沈黙でよい）。無理に話題を変えない。
              - 「記憶リスト」「保存済み」「設定の記憶」などメタな言い方はしない。
              """.Trim()
            : """
              How to use:
              - Treat these as things you already know about the user — not documents or settings UI.
              - Only when they clearly connect to this user turn, mention at most one as a soft callback (e.g. "you mentioned that before").
              - If the link is weak or missing, stay silent. Do not force a topic change.
              - Do not talk about memory lists, saved memories, or settings.
              """.Trim();
        return header + "\n" + guidance + "\n" + string.Join("\n", lines);
    }

    /// <summary>セッションから記憶を抽出し、新規に保存できた件数を返す。プレーンAIセッションは 0。</summary>
    public async Task<int> ExtractFromSessionAsync(
        string sessionId,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        var settings = _appSettings.Load();
        if (!settings.MemoryEnabled || !settings.MemoryAutoExtractOnClose)
            return 0;

        var presetKey = ResolvePresetKeyForSession(sessionId);
        if (!SupportsLongTermMemory(presetKey))
            return 0;

        if (!await _llama.PingAsync(ct))
            return 0;

        var transcript = BuildTranscript(messages, 2000);
        if (transcript.Length == 0)
            return 0;

        var prompt = new List<ChatTurn>
        {
            new(
                "system",
                """
                Extract 0 to 3 short facts worth remembering about the user for future chats.
                Prefer personal feelings, promises, nicknames, preferences, relationship tone, hobbies, worries — not only schedules.
                Do NOT invent facts. Skip pure small talk or roleplay instructions that are not about the user.
                Output one fact per line. No bullets, no numbering, no quotes.
                If nothing is worth remembering, output exactly: NONE
                Use the same language as the conversation. Max 80 characters per line.
                """.Trim()),
            new("user", transcript),
        };

        var saved = 0;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var raw = await _llama.ChatAsync(
                prompt,
                temperature: 0.2,
                topP: 0.9,
                maxTokens: 160,
                useReasoning: false,
                ct: timeout.Token);

            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Length < 4)
                    continue;
                if (await AddAsync(line, presetKey!, memoryPath: "session", sourceSessionId: sessionId, ct) is not null)
                    saved++;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Memory extract failed");
        }

        return saved;
    }

    internal void PrepareIndexes(SqliteConnection conn)
    {
        lock (_indexLock)
        {
            _index.PrepareFts(conn);
            _db.PrepareVectors(conn);
            _index.PrepareVec(conn);
        }
    }

    private string? ResolvePresetKeyForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT preset_key FROM conversation_sessions
            WHERE id = $id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    private async Task IndexMemoryAsync(long id, string content, CancellationToken ct)
    {
        // FTS は先に確定（埋め込み失敗で巻き戻さない）
        try
        {
            using var conn = _db.Open();
            conn.Open();
            PrepareIndexes(conn);
            using var tx = conn.BeginTransaction();
            _index.IndexContent(conn, id, content, tx);
            tx.Commit();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Memory FTS index failed");
            return;
        }

        try
        {
            if (!await _llama.EmbeddingsSupportedAsync(ct))
                return;

            var embedding = await _llama.EmbedAsync(content, ct);
            if (embedding is null or { Length: 0 })
                return;

            using var conn = _db.Open();
            conn.Open();
            PrepareIndexes(conn);
            using var tx = conn.BeginTransaction();
            _index.EnsureVectorTable(conn, embedding.Length, tx);
            _index.InsertVector(conn, id, embedding, tx);
            tx.Commit();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Memory vector index failed");
        }
    }

    private static List<UserMemoryRecord> LoadByIds(
        SqliteConnection conn,
        IReadOnlyList<long> ids,
        string presetKey,
        int topK)
    {
        if (ids.Count == 0)
            return [];

        var inClause = string.Join(", ", ids.Select((_, i) => "$id" + i));
        var cmd = conn.CreateCommand();
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("$id" + i, ids[i]);
        cmd.Parameters.AddWithValue("$k", presetKey);

        cmd.CommandText = $"""
            SELECT id, content, memory_path, source_session_id, created_at, preset_key
            FROM user_memories
            WHERE id IN ({inClause})
              AND preset_key = $k
            """;

        var map = new Dictionary<long, UserMemoryRecord>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var rec = ReadRecord(reader);
                map[rec.Id] = rec;
            }
        }

        var ordered = new List<UserMemoryRecord>(Math.Min(topK, ids.Count));
        foreach (var id in ids)
        {
            if (!map.TryGetValue(id, out var rec))
                continue;
            ordered.Add(rec);
            if (ordered.Count >= topK)
                break;
        }

        return ordered;
    }

    private static UserMemoryRecord ReadRecord(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            reader.IsDBNull(3) ? "" : reader.GetString(3),
            reader.GetString(4),
            reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : "");

    private static string BuildTranscript(IReadOnlyList<(string Role, string Content)> messages, int maxChars)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (role, content) in messages)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;
            var line = $"{role}: {content.Trim()}";
            if (sb.Length + line.Length + 1 > maxChars)
                break;
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(line);
        }

        return sb.ToString();
    }
}
