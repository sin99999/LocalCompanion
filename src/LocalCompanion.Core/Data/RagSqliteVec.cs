using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Data;

/// <summary>sqlite-vec（vec0）によるベクトル検索。MIT ライセンス・ファイル1つで完結。</summary>
public sealed class RagSqliteVec
{
    private const string MetaKeyDim = "embedding_dim";

    public bool IsAvailable { get; private set; }

    public void TryPrepare(SqliteConnection conn)
    {
        IsAvailable = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            conn.EnableExtensions();
            conn.LoadVector();
            IsAvailable = true;
            EnsureMetaTable(conn);
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public int? GetStoredDimension(SqliteConnection conn)
    {
        if (!IsAvailable)
            return null;

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM rag_vec_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", MetaKeyDim);
        var raw = cmd.ExecuteScalar()?.ToString();
        return int.TryParse(raw, out var dim) && dim > 0 ? dim : null;
    }

    public void EnsureVectorTable(SqliteConnection conn, int dimension)
    {
        if (!IsAvailable || dimension <= 0)
            return;

        var stored = GetStoredDimension(conn);
        if (stored == dimension && TableExists(conn, "rag_vec"))
            return;

        if (TableExists(conn, "rag_vec"))
        {
            var drop = conn.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS rag_vec";
            drop.ExecuteNonQuery();
        }

        var create = conn.CreateCommand();
        create.CommandText = $"CREATE VIRTUAL TABLE rag_vec USING vec0(embedding float[{dimension}])";
        create.ExecuteNonQuery();

        SetMeta(conn, MetaKeyDim, dimension.ToString());
        RebuildFromLegacyEmbeddings(conn, dimension);
    }

    public void InsertVector(SqliteConnection conn, long chunkId, float[] embedding, SqliteTransaction? transaction = null)
    {
        if (!IsAvailable || !TableExists(conn, "rag_vec"))
            return;

        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO rag_vec(rowid, embedding) VALUES ($id, $emb)";
        cmd.Parameters.AddWithValue("$id", chunkId);
        cmd.Parameters.AddWithValue("$emb", JsonSerializer.Serialize(embedding));
        cmd.ExecuteNonQuery();
    }

    public void DeleteVectorsForSource(SqliteConnection conn, string source, SqliteTransaction? transaction = null)
    {
        if (!IsAvailable || !TableExists(conn, "rag_vec"))
            return;

        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            DELETE FROM rag_vec
            WHERE rowid IN (SELECT id FROM rag_chunks WHERE source = $s)
            """;
        cmd.Parameters.AddWithValue("$s", source);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<long> Search(SqliteConnection conn, float[] query, int topK, IReadOnlyList<string>? sourcesFilter = null)
    {
        if (!IsAvailable || !TableExists(conn, "rag_vec") || topK <= 0)
            return Array.Empty<long>();

        if (sourcesFilter is { Count: 0 })
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        if (sourcesFilter is { Count: > 0 })
        {
            var inClause = BuildInClause(cmd, sourcesFilter, "src");
            cmd.CommandText = $"""
                SELECT v.rowid
                FROM rag_vec v
                INNER JOIN rag_chunks c ON c.id = v.rowid
                WHERE c.source IN ({inClause})
                  AND v.embedding MATCH $q
                ORDER BY distance
                LIMIT $k
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT rowid
                FROM rag_vec
                WHERE embedding MATCH $q
                ORDER BY distance
                LIMIT $k
                """;
        }

        cmd.Parameters.AddWithValue("$q", JsonSerializer.Serialize(query));
        cmd.Parameters.AddWithValue("$k", topK);

        var ids = new List<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
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

    public IReadOnlyList<long> Search(SqliteConnection conn, float[] query, int topK) =>
        Search(conn, query, topK, sourcesFilter: null);

    private static void EnsureMetaTable(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rag_vec_meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void SetMeta(SqliteConnection conn, string key, string value)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_vec_meta(key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, string name)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE name = $n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static void RebuildFromLegacyEmbeddings(SqliteConnection conn, int dimension)
    {
        var select = conn.CreateCommand();
        select.CommandText = "SELECT id, embedding FROM rag_chunks";
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var vec = JsonSerializer.Deserialize<float[]>(reader.GetString(1));
            if (vec is null || vec.Length != dimension)
                continue;

            var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO rag_vec(rowid, embedding) VALUES ($id, $emb)";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$emb", JsonSerializer.Serialize(vec));
            insert.ExecuteNonQuery();
        }
    }
}
