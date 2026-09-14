using LocalCompanion.Data;

namespace LocalCompanion.Services;

/// <summary>conversation_sessions の履歴ダイジェスト列の出し入れ。</summary>
internal sealed class ChatHistoryDigestStore
{
    private readonly RagDatabase _db;

    public ChatHistoryDigestStore(RagDatabase db) => _db = db;

    public (string Digest, long UptoId) Read(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return ("", 0);

        using var conn = _db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT history_digest, history_digest_upto_id
            FROM conversation_sessions
            WHERE id = $id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return ("", 0);

        var digest = r.IsDBNull(0) ? "" : r.GetString(0);
        var upto = r.IsDBNull(1) ? 0L : r.GetInt64(1);
        return (digest ?? "", upto);
    }

    public void Write(string sessionId, string digest, long uptoId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        using var conn = _db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE conversation_sessions
            SET history_digest = $d, history_digest_upto_id = $u
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$d", digest);
        cmd.Parameters.AddWithValue("$u", uptoId);
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }
}
