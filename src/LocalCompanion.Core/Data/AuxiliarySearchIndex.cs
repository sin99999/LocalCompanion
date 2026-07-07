using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Data;

/// <summary>memory / chat_message 向けの FTS5 + vec0 インデックス。</summary>
public sealed class AuxiliarySearchIndex
{
    private readonly string _ftsTable;
    private readonly string _vecTable;
    private readonly string _metaDimKey;

    public AuxiliarySearchIndex(string ftsTable, string vecTable, string metaDimKey)
    {
        _ftsTable = ftsTable;
        _vecTable = vecTable;
        _metaDimKey = metaDimKey;
    }

    public bool FtsAvailable { get; private set; }

    public bool VecAvailable { get; private set; }

    public void PrepareFts(SqliteConnection conn)
    {
        FtsAvailable = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS {_ftsTable} USING fts5(
                  content,
                  tokenize='unicode61'
                );
                """;
            cmd.ExecuteNonQuery();
            FtsAvailable = true;
        }
        catch
        {
            FtsAvailable = false;
        }
    }

    public void PrepareVec(SqliteConnection conn)
    {
        VecAvailable = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            conn.EnableExtensions();
            conn.LoadVector();
            VecAvailable = true;
            EnsureMetaTable(conn);
        }
        catch
        {
            VecAvailable = false;
        }
    }

    public void EnsureVectorTable(SqliteConnection conn, int dimension, SqliteTransaction? transaction = null)
    {
        if (!VecAvailable || dimension <= 0)
            return;

        var stored = GetStoredDimension(conn);
        if (stored == dimension && TableExists(conn, _vecTable))
            return;

        if (TableExists(conn, _vecTable))
        {
            var drop = conn.CreateCommand();
            drop.Transaction = transaction;
            drop.CommandText = $"DROP TABLE IF EXISTS {_vecTable}";
            drop.ExecuteNonQuery();
        }

        var create = conn.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = $"CREATE VIRTUAL TABLE {_vecTable} USING vec0(embedding float[{dimension}])";
        create.ExecuteNonQuery();
        SetMeta(conn, _metaDimKey, dimension.ToString(), transaction);
    }

    public void IndexContent(SqliteConnection conn, long rowId, string content, SqliteTransaction? transaction = null)
    {
        if (FtsAvailable)
        {
            var delete = conn.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {_ftsTable} WHERE rowid = $id";
            delete.Parameters.AddWithValue("$id", rowId);
            delete.ExecuteNonQuery();

            var insert = conn.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {_ftsTable}(rowid, content) VALUES ($id, $c)";
            insert.Parameters.AddWithValue("$id", rowId);
            insert.Parameters.AddWithValue("$c", content);
            insert.ExecuteNonQuery();
        }
    }

    public void InsertVector(SqliteConnection conn, long rowId, float[] embedding, SqliteTransaction? transaction = null)
    {
        if (!VecAvailable || !TableExists(conn, _vecTable))
            return;

        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"INSERT INTO {_vecTable}(rowid, embedding) VALUES ($id, $emb)";
        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.Parameters.AddWithValue("$emb", JsonSerializer.Serialize(embedding));
        cmd.ExecuteNonQuery();
    }

    public void DeleteRow(SqliteConnection conn, long rowId, SqliteTransaction? transaction = null)
    {
        if (FtsAvailable)
        {
            var delFts = conn.CreateCommand();
            delFts.Transaction = transaction;
            delFts.CommandText = $"DELETE FROM {_ftsTable} WHERE rowid = $id";
            delFts.Parameters.AddWithValue("$id", rowId);
            delFts.ExecuteNonQuery();
        }

        if (VecAvailable && TableExists(conn, _vecTable))
        {
            var delVec = conn.CreateCommand();
            delVec.Transaction = transaction;
            delVec.CommandText = $"DELETE FROM {_vecTable} WHERE rowid = $id";
            delVec.Parameters.AddWithValue("$id", rowId);
            delVec.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<long> SearchFts(SqliteConnection conn, string matchQuery, int limit)
    {
        if (!FtsAvailable || limit <= 0 || string.IsNullOrWhiteSpace(matchQuery))
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT rowid
            FROM {_ftsTable}
            WHERE {_ftsTable} MATCH $q
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

    public IReadOnlyList<long> SearchVec(SqliteConnection conn, float[] query, int topK)
    {
        if (!VecAvailable || !TableExists(conn, _vecTable) || topK <= 0)
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT rowid
            FROM {_vecTable}
            WHERE embedding MATCH $q AND k = $k
            ORDER BY distance
            """;
        cmd.Parameters.AddWithValue("$q", JsonSerializer.Serialize(query));
        cmd.Parameters.AddWithValue("$k", topK);

        var ids = new List<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    public int? GetStoredDimension(SqliteConnection conn)
    {
        if (!VecAvailable)
            return null;

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM rag_vec_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", _metaDimKey);
        var raw = cmd.ExecuteScalar()?.ToString();
        return int.TryParse(raw, out var dim) && dim > 0 ? dim : null;
    }

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

    private static void SetMeta(SqliteConnection conn, string key, string value, SqliteTransaction? transaction = null)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
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
}
