using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

public sealed class AuxiliarySearchIndexTests
{
    [Fact]
    public void FtsIndexesAndSearchesContent()
    {
        var index = new AuxiliarySearchIndex("test_fts", "test_vec", "test_dim");
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        index.PrepareFts(conn);
        Assert.True(index.FtsAvailable);

        index.IndexContent(conn, 1, "UTF-8 の刑法 VERBATIM テスト");
        index.IndexContent(conn, 2, "七夕のファイル書き出し");

        var match = RagSqliteFts.BuildMatchQuery("刑法 VERBATIM");
        var hits = index.SearchFts(conn, match, 5);
        Assert.Contains(1L, hits);
        Assert.DoesNotContain(2L, hits);
    }
}
