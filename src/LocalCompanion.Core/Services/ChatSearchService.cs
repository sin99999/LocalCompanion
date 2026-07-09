using LocalCompanion.Data;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class ChatSearchService
{
    private readonly RagDatabase _db;
    private readonly LlamaServerClient _llama;
    private readonly AppSettingsStore _appSettings;
    private readonly LlamaOptions _opt;
    private readonly AuxiliarySearchIndex _index = new("chat_msg_fts", "chat_msg_vec", "chat_msg_embedding_dim");
    private readonly object _indexLock = new();

    public ChatSearchService(
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

    public bool IsEnabled => _appSettings.Load().ChatSearchEnabled;

    public async Task IndexMessageAsync(long messageId, string content, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return;

        var trimmed = content.Trim();
        if (trimmed.Length == 0)
            return;

        if (!await _llama.EmbeddingsSupportedAsync(ct))
        {
            IndexFtsOnly(messageId, trimmed);
            return;
        }

        var embedding = await _llama.EmbedAsync(trimmed, ct);
        if (embedding is null or { Length: 0 })
        {
            IndexFtsOnly(messageId, trimmed);
            return;
        }

        using var conn = _db.Open();
        conn.Open();
        PrepareIndexes(conn);
        using var tx = conn.BeginTransaction();
        _index.IndexContent(conn, messageId, trimmed, tx);
        _index.EnsureVectorTable(conn, embedding.Length, tx);
        _index.InsertVector(conn, messageId, embedding, tx);
        tx.Commit();
    }

    public async Task<IReadOnlyList<ChatSearchHit>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<ChatSearchHit>();

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

        return LoadHits(conn, fused);
    }

    /// <summary>セッション削除時に FTS / vec の孤立行を掃除します。</summary>
    public void DeleteIndexedMessages(SqliteConnection conn, IReadOnlyList<long> messageIds, SqliteTransaction? transaction = null)
    {
        if (messageIds.Count == 0)
            return;

        PrepareIndexes(conn);
        foreach (var id in messageIds)
            _index.DeleteRow(conn, id, transaction);
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

    private void IndexFtsOnly(long messageId, string content)
    {
        using var conn = _db.Open();
        conn.Open();
        PrepareIndexes(conn);
        _index.IndexContent(conn, messageId, content);
    }

    private static List<ChatSearchHit> LoadHits(SqliteConnection conn, IReadOnlyList<long> messageIds)
    {
        if (messageIds.Count == 0)
            return [];

        var inClause = string.Join(", ", messageIds.Select((_, i) => "$id" + i));
        var cmd = conn.CreateCommand();
        for (var i = 0; i < messageIds.Count; i++)
            cmd.Parameters.AddWithValue("$id" + i, messageIds[i]);

        cmd.CommandText = $"""
            SELECT m.id, m.session_id, m.preset_key, m.role, m.content, m.created_at,
                   COALESCE(s.title, '')
            FROM chat_messages m
            LEFT JOIN conversation_sessions s ON s.id = m.session_id
            WHERE m.id IN ({inClause})
            """;

        var map = new Dictionary<long, ChatSearchHit>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                map[id] = new ChatSearchHit(
                    id,
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(6) ? "" : reader.GetString(6),
                    reader.GetString(5));
            }
        }

        var ordered = new List<ChatSearchHit>(messageIds.Count);
        foreach (var id in messageIds)
        {
            if (map.TryGetValue(id, out var hit))
                ordered.Add(hit);
        }

        return ordered;
    }
}
