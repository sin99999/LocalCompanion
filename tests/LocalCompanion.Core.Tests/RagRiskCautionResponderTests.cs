using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagRiskCautionResponderTests
{
    [Fact]
    public void TryFormat_RiskCautionWithTheftPenalty_QuotesMechanically()
    {
        const string penalty =
            "他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。";
        var hits = new[]
        {
            new RagSearchHit(
                penalty,
                "刑法.md",
                "第235条（窃盗）",
                1,
                "c235",
                PenaltyLead: penalty,
                VerbatimQuote: penalty),
        };

        Assert.True(RagRiskCautionResponder.TryFormat(
            RagConversationMode.RiskCaution, hits, "万引きしたら捕まる？", japanese: true, out var reply));
        Assert.Contains(penalty, reply, StringComparison.Ordinal);
        Assert.Contains("やり方は教えない", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void PickBestHit_MurderQuery_DoesNotPreferTheft()
    {
        const string theft =
            "他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。";
        const string murder =
            "人を殺した者は、死刑又は無期若しくは五年以上の懲役に処する。";
        var hits = new[]
        {
            new RagSearchHit(theft, "刑法.md", "第5条（窃盗）", 1, "c5", PenaltyLead: theft),
            new RagSearchHit(murder, "刑法.md", "第199条（殺人）", 1, "c199", PenaltyLead: murder),
        };

        var pick = RagRiskCautionResponder.PickBestHit(hits, "人を仕方なく殺す(ナイフ)場合は捕まる？");
        Assert.NotNull(pick);
        Assert.Contains("殺人", pick.Value.Hit.HeaderText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFormat_AbductionQuery_QuotesAbductionNotTheft()
    {
        const string theft =
            "他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。";
        const string abduction =
            "未成年者を略取した者は、三月以上七年以下の懲役に処する。";
        var hits = new[]
        {
            new RagSearchHit(theft, "刑法.md", "第5条（窃盗）", 1, "c5", PenaltyLead: theft),
            new RagSearchHit(abduction, "刑法.md", "第12条（略取）", 1, "c12", PenaltyLead: abduction),
        };

        Assert.True(RagRiskCautionResponder.TryFormat(
            RagConversationMode.RiskCaution,
            hits,
            "未成年略取とか無かった？",
            japanese: true,
            out var reply));
        Assert.Contains("略取", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("窃取", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFormat_NonConsensualSexQuery_QuotesMatchingArticle()
    {
        const string body =
            "本人の意思に反して性的な行為を行うことを不同意性交という。五年以上の有期懲役に処する。";
        var hits = new[]
        {
            new RagSearchHit(body, "刑法.md", "第14条（不同意性交）", 1, "c14"),
        };

        Assert.True(RagRiskCautionResponder.TryFormat(
            RagConversationMode.RiskCaution,
            hits,
            "不同意性交とかは？",
            japanese: true,
            out var reply));
        Assert.Contains("不同意性交", reply, StringComparison.Ordinal);
        Assert.Contains("【資料記載の罰則・条文】", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectCrimeFamily_AbductionAndSexual()
    {
        Assert.Equal(
            RagRiskCautionResponder.CrimeFamily.Abduction,
            RagRiskCautionResponder.DetectCrimeFamily("未成年略取とか無かった？"));
        Assert.Equal(
            RagRiskCautionResponder.CrimeFamily.SexualOffense,
            RagRiskCautionResponder.DetectCrimeFamily("不同意性交とかは？"));
    }
}
