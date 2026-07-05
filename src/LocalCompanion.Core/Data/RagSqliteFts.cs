using Microsoft.Data.Sqlite;

namespace LocalCompanion.Data;

/// <summary>SQLite FTS5 によるキーワード検索（条文番号・固有名詞向け）。</summary>
public sealed class RagSqliteFts
{
    public bool IsAvailable { get; private set; }

    public void TryPrepare(SqliteConnection conn)
    {
        IsAvailable = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            EnsureTable(conn);
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public void EnsureTable(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS rag_fts USING fts5(
              text,
              header_text,
              chapter,
              section,
              tokenize='unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void IndexChunk(
        SqliteConnection conn,
        long chunkId,
        string text,
        string headerText,
        string chapter,
        string section,
        SqliteTransaction? transaction = null)
    {
        if (!IsAvailable)
            return;

        var delete = conn.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM rag_fts WHERE rowid = $id";
        delete.Parameters.AddWithValue("$id", chunkId);
        delete.ExecuteNonQuery();

        var insert = conn.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO rag_fts(rowid, text, header_text, chapter, section)
            VALUES ($id, $t, $h, $ch, $sec)
            """;
        insert.Parameters.AddWithValue("$id", chunkId);
        insert.Parameters.AddWithValue("$t", text);
        insert.Parameters.AddWithValue("$h", headerText);
        insert.Parameters.AddWithValue("$ch", chapter);
        insert.Parameters.AddWithValue("$sec", section);
        insert.ExecuteNonQuery();
    }

    public void DeleteForSource(SqliteConnection conn, string source, SqliteTransaction? transaction = null)
    {
        if (!IsAvailable)
            return;

        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            DELETE FROM rag_fts
            WHERE rowid IN (SELECT id FROM rag_chunks WHERE source = $s)
            """;
        cmd.Parameters.AddWithValue("$s", source);
        cmd.ExecuteNonQuery();
    }

    public void Backfill(SqliteConnection conn)
    {
        if (!IsAvailable)
            return;

        var clear = conn.CreateCommand();
        clear.CommandText = "DELETE FROM rag_fts";
        clear.ExecuteNonQuery();

        var select = conn.CreateCommand();
        select.CommandText = """
            SELECT id, text, header_text, chapter, section
            FROM rag_chunks
            """;
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            IndexChunk(
                conn,
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4));
        }

        Optimize(conn);
    }

    public void Optimize(SqliteConnection conn)
    {
        if (!IsAvailable)
            return;

        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO rag_fts(rag_fts) VALUES('optimize')";
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<long> Search(
        SqliteConnection conn,
        string matchQuery,
        int limit,
        IReadOnlyList<string> enabledSources)
    {
        if (!IsAvailable || limit <= 0 || string.IsNullOrWhiteSpace(matchQuery))
            return Array.Empty<long>();

        if (enabledSources.Count == 0)
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        var inClause = BuildInClause(cmd, enabledSources, "src");
        cmd.CommandText = $"""
            SELECT f.rowid
            FROM rag_fts f
            INNER JOIN rag_chunks c ON c.id = f.rowid
            WHERE rag_fts MATCH $q
              AND c.source IN ({inClause})
            ORDER BY rank
            LIMIT $k
            """;
        cmd.Parameters.AddWithValue("$q", matchQuery);
        cmd.Parameters.AddWithValue("$k", limit);

        var ids = new List<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    internal static string BuildMatchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeFtsToken)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList();

        if (tokens.Count == 0)
            return "";

        return string.Join(" OR ", tokens.Select(t => $"\"{t}\""));
    }

    private static string SanitizeFtsToken(string token)
    {
        return token
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal)
            .Replace("(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal);
    }

    private static string BuildInClause(SqliteCommand cmd, IReadOnlyList<string> values, string prefix)
    {
        var parts = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var name = $"${prefix}{i}";
            parts.Add(name);
            cmd.Parameters.AddWithValue(name, values[i]);
        }

        return string.Join(", ", parts);
    }
}
