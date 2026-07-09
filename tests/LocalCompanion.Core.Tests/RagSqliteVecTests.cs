using System.Text.Json;
using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

public sealed class RagSqliteVecTests
{
    [Fact]
    public void Search_WithSourceFilter_DoesNotThrowAndReturnsFilteredIds()
    {
        var vec = new RagSqliteVec();
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        vec.TryPrepare(conn);
        Assert.True(vec.IsAvailable, "vec0.dll must be present in the test output (see LocalCompanion.Core.Tests.csproj Content).");

        CreateChunkTable(conn);
        const int dim = 4;
        vec.EnsureVectorTable(conn, dim);

        InsertChunk(conn, 1, "penal-code.md", MakeVector(1f, 0f, 0f, 0f));
        InsertChunk(conn, 2, "penal-code.md", MakeVector(0.9f, 0.1f, 0f, 0f));
        InsertChunk(conn, 3, "labor-law.md", MakeVector(0f, 1f, 0f, 0f));

        vec.InsertVector(conn, 1, MakeVector(1f, 0f, 0f, 0f));
        vec.InsertVector(conn, 2, MakeVector(0.9f, 0.1f, 0f, 0f));
        vec.InsertVector(conn, 3, MakeVector(0f, 1f, 0f, 0f));

        var query = MakeVector(1f, 0f, 0f, 0f);
        var filtered = vec.Search(conn, query, topK: 2, sourcesFilter: ["penal-code.md"]);
        var unfiltered = vec.Search(conn, query, topK: 2);

        Assert.NotEmpty(filtered);
        Assert.DoesNotContain(3L, filtered);
        Assert.True(unfiltered.Count >= filtered.Count);
    }

    [Fact]
    public void Search_EmptySourceFilter_ReturnsEmpty()
    {
        var vec = new RagSqliteVec();
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        vec.TryPrepare(conn);
        Assert.True(vec.IsAvailable, "vec0.dll must be present in the test output (see LocalCompanion.Core.Tests.csproj Content).");

        var ids = vec.Search(conn, MakeVector(1, 0, 0, 0), topK: 3, sourcesFilter: Array.Empty<string>());
        Assert.Empty(ids);
    }

    private static void CreateChunkTable(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE rag_chunks (
              id INTEGER PRIMARY KEY,
              source TEXT NOT NULL,
              text TEXT NOT NULL,
              embedding TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertChunk(SqliteConnection conn, long id, string source, float[] embedding)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_chunks (id, source, text, embedding, created_at)
            VALUES ($id, $source, $text, $emb, $at)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$text", "sample");
        cmd.Parameters.AddWithValue("$emb", JsonSerializer.Serialize(embedding));
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static float[] MakeVector(float a, float b, float c, float d) => [a, b, c, d];
}
