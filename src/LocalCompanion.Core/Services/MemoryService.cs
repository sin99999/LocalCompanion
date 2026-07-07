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

    public IReadOnlyList<UserMemoryRecord> List(int limit = 100)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, content, memory_path, source_session_id, created_at
            FROM user_memories
            ORDER BY updated_at DESC
            LIMIT $k
            """;
        cmd.Parameters.AddWithValue("$k", Math.Max(1, limit));

        var list = new List<UserMemoryRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UserMemoryRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.GetString(4)));
        }

        return list;
    }

    public async Task<long?> AddAsync(string content, string? memoryPath = null, string? sourceSessionId = null, CancellationToken ct = default)
    {
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
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO user_memories (content, memory_path, source_session_id, created_at, updated_at)
                VALUES ($c, $p, $s, $t, $t)
                """;
            cmd.Parameters.AddWithValue("$c", trimmed);
            cmd.Parameters.AddWithValue("$p", memoryPath ?? "");
            cmd.Parameters.AddWithValue("$s", sourceSessionId ?? "");
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

    public async Task<IReadOnlyList<UserMemoryRecord>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<UserMemoryRecord>();

        using var conn = _db.Open();
        conn.Open();
        PrepareIndexes(conn);

        var pool = Math.Max(topK * 4, _opt.RagSearchPoolSize / 2);
        var ftsQuery = RagSqliteFts.BuildMatchQuery(query);
        var ftsIds = string.IsNullOrEmpty(ftsQuery)
            ? Array.Empty<long>()
            : _index.SearchFts(conn, ftsQuery, pool);

        IReadOnlyList<long> vecIds = Array.Empty<long>();
        if (await _llama.EmbeddingsSupportedAsync(ct))
        {
            var embedding = await _llama.EmbedAsync(query, ct);
            if (embedding is { Length: > 0 })
            {
                _index.EnsureVectorTable(conn, embedding.Length);
                vecIds = _index.SearchVec(conn, embedding, pool);
            }
        }

        var fused = RagHybridSearch.FuseRrf(
            ftsIds,
            vecIds,
            topK,
            _opt.RagRrfK,
            _opt.RagWeightFts,
            _opt.RagWeightVec);

        if (fused.Count == 0)
            return Array.Empty<UserMemoryRecord>();

        return LoadByIds(conn, fused);
    }

    public async Task<IReadOnlyList<UserMemoryRecord>> GetRelevantForPromptAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return Array.Empty<UserMemoryRecord>();

        var topK = Math.Clamp(_opt.MemoryTopK, 1, 12);
        return await SearchAsync(userMessage, topK, ct);
    }

    public static string FormatForSystemPrompt(IReadOnlyList<UserMemoryRecord> memories, bool japanese)
    {
        if (memories.Count == 0)
            return "";

        var header = japanese
            ? "【ユーザーについて覚えていること（長期記憶）】"
            : "[Long-term memories about the user]";
        var lines = memories.Select(m => "- " + m.Content.Trim());
        return header + "\n" + string.Join("\n", lines);
    }

    public async Task ExtractFromSessionAsync(
        string sessionId,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        var settings = _appSettings.Load();
        if (!settings.MemoryEnabled || !settings.MemoryAutoExtractOnClose)
            return;

        if (!await _llama.PingAsync(ct))
            return;

        var transcript = BuildTranscript(messages, 2000);
        if (transcript.Length == 0)
            return;

        var prompt = new List<ChatTurn>
        {
            new(
                "system",
                """
                Extract 0 to 3 short facts worth remembering about the user for future chats.
                Output one fact per line. No bullets, no numbering, no quotes.
                If nothing is worth remembering, output exactly: NONE
                Use the same language as the conversation. Max 80 characters per line.
                """.Trim()),
            new("user", transcript),
        };

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
                return;

            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Length < 4)
                    continue;
                await AddAsync(line, memoryPath: "session", sourceSessionId: sessionId, ct);
            }
        }
        catch
        {
            /* optional extraction */
        }
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

    private async Task IndexMemoryAsync(long id, string content, CancellationToken ct)
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
        _index.IndexContent(conn, id, content, tx);
        _index.EnsureVectorTable(conn, embedding.Length, tx);
        _index.InsertVector(conn, id, embedding, tx);
        tx.Commit();
    }

    private static List<UserMemoryRecord> LoadByIds(SqliteConnection conn, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
            return [];

        var inClause = string.Join(", ", ids.Select((_, i) => "$id" + i));
        var cmd = conn.CreateCommand();
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("$id" + i, ids[i]);

        cmd.CommandText = $"""
            SELECT id, content, memory_path, source_session_id, created_at
            FROM user_memories
            WHERE id IN ({inClause})
            """;

        var map = new Dictionary<long, UserMemoryRecord>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                map[id] = new UserMemoryRecord(
                    id,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.GetString(4));
            }
        }

        var ordered = new List<UserMemoryRecord>(ids.Count);
        foreach (var id in ids)
        {
            if (map.TryGetValue(id, out var rec))
                ordered.Add(rec);
        }

        return ordered;
    }

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
