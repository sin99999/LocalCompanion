using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>
/// embed 失敗時もチャンクを FTS に載せられること（IngestTextAsync の空 embedding + IndexChunk 経路）を守る。
/// </summary>
public sealed class RagEmbedSkipFtsTests
{
    [Fact]
    public void IndexChunk_WithEmptyEmbeddingJson_StillSearchableViaFts()
    {
        var fts = new RagSqliteFts();
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        CreateChunkTable(conn);
        fts.TryPrepare(conn);
        Assert.True(fts.IsAvailable);

        const long id = 42;
        InsertChunkWithEmptyEmbedding(conn, id, "notes.md", "embed-skip FTS fallback sample text");
        fts.IndexChunk(conn, id, "embed-skip FTS fallback sample text", headerText: "", chapter: "", section: "");

        var match = RagSqliteFts.BuildMatchQuery("embed-skip fallback");
        var hits = fts.Search(conn, match, limit: 5, enabledSources: ["notes.md"]);

        Assert.Contains(id, hits);
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
              created_at TEXT NOT NULL,
              header_text TEXT,
              chapter TEXT,
              section TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertChunkWithEmptyEmbedding(SqliteConnection conn, long id, string source, string text)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_chunks (id, source, text, embedding, created_at, header_text, chapter, section)
            VALUES ($id, $source, $text, '[]', $at, '', '', '')
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
