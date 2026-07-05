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
}
