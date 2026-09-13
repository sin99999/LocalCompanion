using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagOverviewQueryParserTests
{
    [Theory]
    [InlineData("登録してある資料全体で、主なテーマを教えて")]
    [InlineData("この資料には何が書いてあるの？概要をまとめて")]
    [InlineData("窃盗と強盗の違いを比較して")]
    [InlineData("What is this document about? Give me an overview")]
    [InlineData("Summarize the main topics")]
    public void TryDetect_OverviewCues_True(string query) =>
        Assert.True(RagOverviewQueryParser.TryDetect(query));

    [Theory]
    [InlineData("刑法4条と104条の違いを比較して")]
    [InlineData("第235条の要点を教えて")]
    [InlineData("残業の法律ってどうなってる？")]
    [InlineData("ビルドはどうやるの？")]
    [InlineData("")]
    public void TryDetect_ArticleNumbersOrPlainQuestions_False(string query) =>
        Assert.False(RagOverviewQueryParser.TryDetect(query));

    [Fact]
    public void Plan_Overview_UsesSynthesisAndKeepsSourceHints()
    {
        var plan = RagQueryPlanner.Plan("刑法全体の主なテーマを教えて", null);

        Assert.Equal(RagQueryIntent.Overview, plan.Intent);
        Assert.Equal(RagResponseMode.Synthesis, plan.ResponseMode);
        Assert.Equal("刑法", plan.SourceHint);
    }

    [Fact]
    public void Plan_SourceCatalogWinsOverOverview()
    {
        var plan = RagQueryPlanner.Plan("登録してある資料の一覧を見せて", null);

        Assert.Equal(RagQueryIntent.SourceCatalog, plan.Intent);
    }

    [Fact]
    public void Plan_ArticleComparison_StaysArticle()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と104条の違いを比較して", null);

        Assert.Equal(RagQueryIntent.Article, plan.Intent);
    }
}
