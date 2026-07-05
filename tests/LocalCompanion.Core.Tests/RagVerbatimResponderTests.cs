using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagVerbatimResponderTests
{
    [Fact]
    public void TryFormat_PenaltyHit_ReturnsDbTextUnmodified()
    {
        const string penalty = "三年以下の懲役又は二百五十万円以下の罰金に処する。";
        var plan = new RagQueryPlan(
            RagQueryIntent.Penalty,
            "贈賄の罰則は？",
            null,
            null,
            "贈賄",
            null,
            null,
            RagResponseMode.Verbatim,
            0.9);
        var hit = new RagSearchHit(
            penalty,
            "刑法.md",
            "第8条（贈賄）",
            0,
            "chunk",
            "",
            penalty,
            penalty);

        Assert.True(RagVerbatimResponder.TryFormat(plan, [hit], japanese: true, out var reply));
        Assert.Contains(penalty, reply);
        Assert.Contains("【資料記載の罰則文言】", reply);
    }

    [Fact]
    public void TryFormat_DefinitionHit_ReturnsDbTextUnmodified()
    {
        const string definition = "光速より速い移動を指す略語。";
        var plan = new RagQueryPlan(
            RagQueryIntent.Definition,
            "FTLとは",
            null,
            null,
            "ftl",
            null,
            null,
            RagResponseMode.Verbatim,
            0.85);
        var hit = new RagSearchHit(
            definition,
            "用語集.md",
            "FTL",
            0,
            "chunk",
            "",
            "",
            "",
            definition);

        Assert.True(RagVerbatimResponder.TryFormat(plan, [hit], japanese: true, out var reply));
        Assert.Contains(definition, reply);
        Assert.Contains("【資料記載の定義】", reply);
    }
}
