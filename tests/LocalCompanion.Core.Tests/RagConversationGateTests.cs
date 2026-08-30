using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagConversationGateTests
{
    [Fact]
    public void Resolve_CasualChitchat_SkipsRag()
    {
        var plan = RagQueryPlanner.Plan("AIさんは女の子ですか？可愛らしく感じます。", null);
        Assert.Equal(RagQueryIntent.General, plan.Intent);
        Assert.Equal(
            RagConversationMode.Skip,
            RagConversationGate.Resolve(plan, "AIさんは女の子ですか？可愛らしく感じます。"));
    }

    [Fact]
    public void Resolve_CrimeRisk_UsesRiskCaution()
    {
        var message = "万引きしたら捕まる？";
        var plan = RagQueryPlanner.Plan(message, null);
        Assert.Equal(RagConversationMode.RiskCaution, RagConversationGate.Resolve(plan, message));
        Assert.True(RagConversationGate.LooksLikeCrimeRisk(message));
    }

    [Fact]
    public void LooksLikeCrimeRisk_AbductionAndNonConsensualSex()
    {
        Assert.True(RagConversationGate.LooksLikeCrimeRisk("未成年略取とか無かった？"));
        Assert.True(RagConversationGate.LooksLikeCrimeRisk("不同意性交とかは？"));
    }

    [Fact]
    public void Resolve_SoftTopic_UsesSoftTopic()
    {
        var message = "残業の法律ってどうなってる？";
        var plan = RagQueryPlanner.Plan(message, null);
        Assert.Equal(RagQueryIntent.General, plan.Intent);
        Assert.Equal(RagConversationMode.SoftTopic, RagConversationGate.Resolve(plan, message));
    }

    [Fact]
    public void Resolve_ArticleQuery_IsStructured()
    {
        var message = "刑法第235条の全文を教えて";
        var plan = RagQueryPlanner.Plan(message, null);
        Assert.Equal(RagConversationMode.Structured, RagConversationGate.Resolve(plan, message));
    }

    [Fact]
    public void FilterWeakHits_DropsUnrelatedPenalCodeChunk()
    {
        var hits = new[]
        {
            new RagSearchHit(
                "第1条（未遂罪）前三条の罪の未遂は、罰する。",
                @"C:\docs\刑法.md",
                "第1条（未遂罪）",
                22,
                "c1"),
        };

        var kept = RagConversationGate.FilterWeakHits(hits, "AIさんは女の子ですか？可愛らしく感じます。");
        Assert.Empty(kept);
    }

    [Fact]
    public void ApplyHitPolicy_RiskCaution_PrefersLegalSourceEvenWithoutTokenOverlap()
    {
        var hits = new[]
        {
            new RagSearchHit("雑談メモ", @"C:\docs\notes.md", "メモ", 1, "n1"),
            new RagSearchHit("第235条 他人の財物を窃取した者は、窃盗の罪とする。", @"C:\docs\刑法.md", "第235条", 1, "c1"),
        };
        var plan = new RagQueryPlan(
            RagQueryIntent.General,
            "万引きしたら捕まる？",
            null,
            null,
            null,
            null,
            null,
            RagResponseMode.Synthesis,
            0.5);
        var result = new RagSearchResult(hits, plan);
        var filtered = RagConversationGate.ApplyHitPolicy(result, "万引きしたら捕まる？", RagConversationMode.RiskCaution);
        Assert.Single(filtered.Hits);
        Assert.Contains("刑法", filtered.Hits[0].SourceFileName, StringComparison.Ordinal);
    }

    [Fact]
    public void RagRiskCautionInstruction_MentionsGentleWarning()
    {
        var text = ChatSystemPromptTexts.RagRiskCautionInstruction(japanese: true);
        Assert.Contains("気を付けて", text);
        Assert.Contains("資料記載の回答", text);
    }
}
