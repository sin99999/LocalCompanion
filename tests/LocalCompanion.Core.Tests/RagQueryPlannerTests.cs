using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagQueryPlannerTests
{
    [Fact]
    public void Plan_PenaltyQuestion_UsesVerbatimMode()
    {
        var plan = RagQueryPlanner.Plan("贈賄の罰則は？", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Penalty, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal("贈賄", plan.TopicKeyword);
    }

    [Fact]
    public void Plan_DefinitionQuestion_UsesVerbatimMode()
    {
        var plan = RagQueryPlanner.Plan("FTLとは", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Definition, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal("ftl", plan.TopicKeyword);
    }

    [Fact]
    public void Plan_AdvisoryQuestion_UsesPersonaSynthesisAndMultipleHints()
    {
        var plan = RagQueryPlanner.Plan(
            "副業禁止の会社だけど100億で買収されたい。就業規則と税法どう思う？",
            previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Advisory, plan.Intent);
        Assert.Equal(RagResponseMode.PersonaSynthesis, plan.ResponseMode);
        Assert.Contains("就業規則", plan.SourceHints!);
        Assert.Contains("税法", plan.SourceHints!);
    }

    [Fact]
    public void Plan_FollowUp_CarriesPreviousTopicInEffectiveQuery()
    {
        var plan = RagQueryPlanner.Plan("RAGを参照して正確に", "贈賄の罰則は？");
        Assert.Contains("贈賄", plan.EffectiveQuery);
    }

    [Fact]
    public void Plan_CasualArticleQuestion_UsesVerbatimNotCitationFirst()
    {
        var plan = RagQueryPlanner.Plan("刑法4条はなーんだ？", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal(400, plan.ArticleSortKey);
    }

    [Fact]
    public void Plan_BareArticleQuestion_UsesVerbatim()
    {
        var plan = RagQueryPlanner.Plan("4条って何？", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal(400, plan.ArticleSortKey);
    }

    [Fact]
    public void Plan_TwoNamedStatutes_KeepsBothSourceHints()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と民法4条", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Contains("刑法", plan.SourceHints!);
        Assert.Contains("民法", plan.SourceHints!);
        Assert.True(RagArticleHitFilter.WantsAllNamedSources(plan));
        Assert.False(RagArticleHitFilter.IsAmbiguousSources(plan, []));
    }

    [Fact]
    public void Plan_PairedStatutes_BindsEachLawToItsArticle()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と民法104条", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.NotNull(plan.ArticleBindings);
        Assert.Equal(2, plan.ArticleBindings!.Count);
        Assert.Equal(400, plan.ArticleBindings[0].SortKey);
        Assert.Equal("刑法", plan.ArticleBindings[0].Hint);
        Assert.Equal(10400, plan.ArticleBindings[1].SortKey);
        Assert.Equal("民法", plan.ArticleBindings[1].Hint);
    }

    [Fact]
    public void Plan_TwoArticles_KeepsBothSortKeys()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と104条の違い", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal([400L, 10400L], plan.ArticleSortKeys);
    }

    [Fact]
    public void Plan_ExtraterritorialConcept_UsesPenaltyTopic()
    {
        var plan = RagQueryPlanner.Plan("国外犯ってどうなる？", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Penalty, plan.Intent);
        Assert.Equal("国外犯", plan.TopicKeyword);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
    }
}
