using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>回答ルール表: 資料の種類とモードで足す指示が決まる。キャラクター有無では変わらない。</summary>
public sealed class RagAnswerPolicyTests
{
    [Fact]
    public void Decide_GeneralHitsFromReadme_NoLegalNoRisk()
    {
        var plan = RagQueryPlanner.Plan("ビルドはどうやるの？", null);
        var hits = new[] { new RagSearchHit("dotnet build を実行します。", "readme.md", "ビルド", 0, "r1") };

        var rules = RagAnswerPolicy.Decide(plan, RagConversationMode.Probe, hits);

        Assert.False(rules.Legal);
        Assert.False(rules.QuotePenalty);
        Assert.False(rules.SingleArticle);
        Assert.False(rules.Risk);
        Assert.False(rules.Advisory);
    }

    [Fact]
    public void Decide_PenaltyLeadHit_RequiresLegalAndQuote()
    {
        var plan = RagQueryPlanner.Plan("残業の法律ってどうなってる？", null);
        var hits = new[]
        {
            new RagSearchHit("他人の財物を窃取した者は…", "刑法.md", "第235条（窃盗）", 0, "c1", PenaltyLead: "十年以下の懲役又は五十万円以下の罰金に処する。"),
        };

        var rules = RagAnswerPolicy.Decide(plan, RagConversationMode.SoftTopic, hits);

        Assert.True(rules.Legal);
        Assert.True(rules.QuotePenalty);
        Assert.False(rules.SingleArticle);
    }

    [Fact]
    public void Decide_ArticleIntent_SingleArticleAndLegal()
    {
        var plan = RagQueryPlanner.Plan("刑法第8条の全文", null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        var hits = new[] { new RagSearchHit("この編の規定は…", "刑法.md", "第8条", 0, "c8") };

        var rules = RagAnswerPolicy.Decide(plan, RagConversationMode.Structured, hits);

        Assert.True(rules.Legal);
        Assert.True(rules.SingleArticle);
    }

    [Fact]
    public void Decide_RiskCautionMode_SetsRisk()
    {
        var plan = RagQueryPlanner.Plan("万引きしたら捕まる？", null);
        var hits = new[] { new RagSearchHit("窃盗の罪", "法令抜粋.md", "第235条", 0, "c1") };

        var rules = RagAnswerPolicy.Decide(plan, RagConversationMode.RiskCaution, hits);

        Assert.True(rules.Risk);
        Assert.True(rules.Legal);
    }

    [Fact]
    public void ChooseEmptyHitInstruction_SilentForProbeAndSoft_SpeaksForStructured()
    {
        Assert.Null(RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.Probe, searchFailed: false, japanese: true));
        Assert.Null(RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.SoftTopic, searchFailed: false, japanese: true));
        Assert.Null(RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.Probe, searchFailed: true, japanese: true));

        var structured = RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.Structured, searchFailed: false, japanese: true);
        Assert.Contains("見つかりませんでした", structured);
        var failed = RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.Structured, searchFailed: true, japanese: true);
        Assert.Contains("検索未完了", failed);

        var risk = RagAnswerPolicy.ChooseEmptyHitInstruction(RagConversationMode.RiskCaution, searchFailed: false, japanese: true);
        Assert.Contains("危険・法令注意", risk);
    }
}
