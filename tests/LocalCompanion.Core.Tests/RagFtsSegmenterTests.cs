using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>日本語 FTS（バイグラム割り）。unicode61 生テキストでは「ビルド」が「ビルドはどうやるの」に当たらなかった退行を防ぐ。</summary>
public sealed class RagFtsSegmenterTests
{
    [Fact]
    public void Segment_KanjiAndKatakanaRuns_BecomeBigrams_HiraganaDropped()
    {
        var segmented = RagFtsSegmenter.Segment("使用者はビルドを労働時間に行う。");

        Assert.Equal("使用 用者 ビル ルド 労働 働時 時間 行", segmented);
    }

    [Fact]
    public void Segment_LatinStaysWholeWord()
    {
        Assert.Equal("WebView2 Runtime 必要", RagFtsSegmenter.Segment("WebView2 Runtime が必要です。"));
    }

    [Fact]
    public void BuildMatchQuery_OneWordIsOnePhrase_WordsJoinedByOr()
    {
        var match = RagFtsSegmenter.BuildMatchQuery("ビルドはどうやるの？");

        Assert.Equal("\"ビル ルド\"", match);

        var two = RagFtsSegmenter.BuildMatchQuery("労働時間の上限");
        Assert.Equal("\"労働 働時 時間\" OR \"上限\"", two);
    }

    [Fact]
    public void BuildMatchQuery_DropsSingleCharAndDigitOnlyWordsWhenOthersExist()
    {
        var match = RagFtsSegmenter.BuildMatchQuery("刑法第8条の罪");

        Assert.Contains("\"刑法 法第\"", match);
        Assert.DoesNotContain("\"8\"", match);
        Assert.DoesNotContain("\"条\"", match);
    }

    [Fact]
    public void BuildMatchQuery_OnlyWeakWords_KeepsThem()
    {
        Assert.Equal("\"第\" OR \"8\" OR \"条\"", RagFtsSegmenter.BuildMatchQuery("第8条"));
        Assert.Equal("", RagFtsSegmenter.BuildMatchQuery("はどう？"));
    }

    [Fact]
    public void ContentRuns_ReturnsKanjiKatakanaLatinOfTwoOrMoreChars()
    {
        var runs = RagFtsSegmenter.ContentRuns("データってどこに保存されるの？WebView2 は？");

        Assert.Equal(["データ", "保存", "WebView2"], runs);
        Assert.Empty(RagFtsSegmenter.ContentRuns("ありがとう、助かった"));
    }

    /// <summary>実際の FTS5 で、ひらがな混じりの質問から本文の語に当たる（索引と質問が同じ割り方）。</summary>
    [Fact]
    public void Fts_JapaneseQuestion_MatchesIndexedBody()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE rag_chunks (id INTEGER PRIMARY KEY, source TEXT NOT NULL)";
            create.ExecuteNonQuery();
            create.CommandText = "INSERT INTO rag_chunks (id, source) VALUES (1, 'readme.md'), (2, 'readme.md')";
            create.ExecuteNonQuery();
        }

        var fts = new RagSqliteFts();
        fts.TryPrepare(conn);
        Assert.True(fts.IsAvailable);
        fts.IndexChunk(conn, 1, "リポジトリのルートで dotnet build を実行します。", "ビルド", "", "");
        fts.IndexChunk(conn, 2, "配布版では exe の横の data フォルダに保存します。", "データの保存場所", "", "");

        var buildHits = fts.Search(conn, RagSqliteFts.BuildSegmentedMatchQuery("ビルドはどうやるの？"), 5, ["readme.md"]);
        Assert.Equal([1L], buildHits);

        var saveHits = fts.Search(conn, RagSqliteFts.BuildSegmentedMatchQuery("データってどこに保存されるの"), 5, ["readme.md"]);
        Assert.Equal([2L], saveHits);

        var articleHits = fts.Search(conn, "\"" + RagFtsSegmenter.Segment("第8条") + "\"", 5, ["readme.md"]);
        Assert.Empty(articleHits);
    }
}
