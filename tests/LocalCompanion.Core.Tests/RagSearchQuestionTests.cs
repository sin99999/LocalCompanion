using LocalCompanion.Data;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

public sealed class RagSearchQuestionTests
{
    private const string Tendon = "手の甲では、外側の指へ向かう腱の一部が隣の指とつながっている。";

    [Fact]
    public void TryAccept_KeepsQuestionWhoseWordsAreNotInThePassage()
    {
        var reply = """
            QUESTION: なぜ薬指は単独で動かせないのか？
            GROUNDED: YES
            """;

        var question = RagSearchQuestion.TryAccept(Tendon, reply);

        Assert.Equal("なぜ薬指は単独で動かせないのか？", question);
    }

    [Fact]
    public void TryAccept_DropsWhenModelSaysNo()
    {
        var reply = """
            QUESTION: なぜ薬指は単独で動かせないのか？
            GROUNDED: NO
            """;

        Assert.Null(RagSearchQuestion.TryAccept(Tendon, reply));
    }

    [Fact]
    public void TryAccept_DropsNoneAndMissingMark()
    {
        Assert.Null(RagSearchQuestion.TryAccept(Tendon, "QUESTION: NONE\nGROUNDED: NO"));
        Assert.Null(RagSearchQuestion.TryAccept(Tendon, "QUESTION: なぜ薬指は単独で動かせないのか？"));
    }

    [Fact]
    public void TryAccept_DropsNumberThatThePassageDoesNotContain()
    {
        var reply = """
            QUESTION: 第99条の罰則は何年か？
            GROUNDED: YES
            """;

        Assert.Null(RagSearchQuestion.TryAccept("第3条 懲役に処する。", reply));
    }

    [Fact]
    public void TryAccept_KeepsNumberThatThePassageContains()
    {
        var reply = """
            QUESTION: 第3条の罰則は何か？
            GROUNDED: YES
            """;

        Assert.Equal("第3条の罰則は何か？", RagSearchQuestion.TryAccept("第3条 懲役に処する。", reply));
    }

    [Fact]
    public void TryAccept_TreatsFullwidthDigitsAsTheSameNumber()
    {
        var reply = """
            QUESTION: 第３条の罰則は何か？
            GROUNDED: YES
            """;

        Assert.Equal("第３条の罰則は何か？", RagSearchQuestion.TryAccept("第3条 懲役に処する。", reply));
    }

    [Fact]
    public void WithQuestion_LeavesEmbeddingTextUntouchedWhenEmpty()
    {
        Assert.Equal("本文", RagSearchQuestion.WithQuestion("本文", ""));
        Assert.Equal("本文\nなぜ？", RagSearchQuestion.WithQuestion("本文", "なぜ？"));
    }

    [Fact]
    public void Fts_QuestionWordsMatchWithoutChangingStoredBody()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE rag_chunks (id INTEGER PRIMARY KEY, source TEXT NOT NULL, text TEXT NOT NULL)";
            create.ExecuteNonQuery();
            create.CommandText = "INSERT INTO rag_chunks (id, source, text) VALUES (1, 'anatomy.md', $t)";
            create.Parameters.AddWithValue("$t", Tendon);
            create.ExecuteNonQuery();
        }

        var fts = new RagSqliteFts();
        fts.TryPrepare(conn);
        fts.IndexChunk(conn, 1, Tendon, "", "", "", searchQuestion: "なぜ薬指は単独で動かせないのか？");

        var hits = fts.Search(conn, RagSqliteFts.BuildSegmentedMatchQuery("薬指はなぜ動かせない？"), 5, ["anatomy.md"]);
        Assert.Equal([1L], hits);

        using var body = conn.CreateCommand();
        body.CommandText = "SELECT text FROM rag_chunks WHERE id = 1";
        Assert.Equal(Tendon, body.ExecuteScalar() as string);
    }

    [Fact]
    public void Cache_ReturnsSavedQuestionForTheSamePassage()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        RagSearchQuestionCache.EnsureTable(conn);
        var hash = RagSearchQuestionCache.Hash(Tendon);

        Assert.Null(RagSearchQuestionCache.TryLoad(conn, hash));
        RagSearchQuestionCache.Save(conn, hash, "なぜ薬指は単独で動かせないのか？");

        Assert.Equal("なぜ薬指は単独で動かせないのか？", RagSearchQuestionCache.TryLoad(conn, hash));
    }
}
