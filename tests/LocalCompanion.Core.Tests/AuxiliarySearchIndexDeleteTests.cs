using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

public sealed class AuxiliarySearchIndexDeleteTests
{
    [Fact]
    public void DeleteRow_RemovesFtsEntry()
    {
        var index = new AuxiliarySearchIndex("del_fts", "del_vec", "del_dim");
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        index.PrepareFts(conn);
        Assert.True(index.FtsAvailable);

        index.IndexContent(conn, 10, "UTF-8 VERBATIM delete-target message");
        index.IndexContent(conn, 11, "keep this other row");

        var match = RagSqliteFts.BuildMatchQuery("VERBATIM delete-target");
        Assert.Contains(10L, index.SearchFts(conn, match, 5));

        index.DeleteRow(conn, 10);
        Assert.DoesNotContain(10L, index.SearchFts(conn, match, 5));
        Assert.Contains(11L, index.SearchFts(conn, RagSqliteFts.BuildMatchQuery("keep"), 5));
    }
}
