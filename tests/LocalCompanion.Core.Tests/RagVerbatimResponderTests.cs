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

    [Fact]
    public void TryFormat_TwoNamedStatutes_IncludesBothBodies()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と民法4条", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("この法律は国外において罪を犯したすべての者に適用する。", "刑法.md", "第4条", 1, "keibo4"),
            new RagSearchHit("未成年者は、法定代理人の同意を得なければ、法律行為をすることができない。", "民法.md", "第4条", 1, "minpo4"),
        };

        Assert.False(RagArticleHitFilter.IsAmbiguousSources(plan, hits));
        Assert.True(RagVerbatimResponder.TryFormat(plan, hits, japanese: true, out var reply));
        Assert.Contains("国外において", reply, StringComparison.Ordinal);
        Assert.Contains("法定代理人", reply, StringComparison.Ordinal);
        Assert.Contains("刑法.md", reply, StringComparison.Ordinal);
        Assert.Contains("民法.md", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFormat_PairedStatutes_DoesNotCrossArticles()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と民法104条", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("刑法4本文", "刑法.md", "第4条", 1, "k4"),
            new RagSearchHit("民法4本文", "民法.md", "第4条", 1, "m4"),
            new RagSearchHit("刑法104本文", "刑法.md", "第104条", 1, "k104"),
            new RagSearchHit("民法104本文", "民法.md", "第104条", 1, "m104"),
        };

        Assert.True(RagVerbatimResponder.TryFormat(plan, hits, japanese: true, out var reply));
        Assert.Contains("刑法4本文", reply, StringComparison.Ordinal);
        Assert.Contains("民法104本文", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("民法4本文", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("刑法104本文", reply, StringComparison.Ordinal);
    }
}
