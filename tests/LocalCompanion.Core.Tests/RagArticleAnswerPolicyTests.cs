using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagArticleAnswerPolicyTests
{
    [Fact]
    public void CasualWhatIsArticle4_UsesVerbatim_AndAllowsCharacterSkipLlm()
    {
        const string query = "刑法4条はなーんだ？";
        var plan = RagQueryPlanner.Plan(query, previousUserMessage: null);

        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.Equal(400, plan.ArticleSortKey);
        Assert.True(RagFormalLegalCue.IsFormalLegalQuery(query));
        Assert.True(RagArticleAnswerPolicy.AllowCharacterVerbatim(isCharacter: true, plan, query));
        Assert.False(
            RagArticleAnswerPolicy.UsePersonaRagInstruction(
                isCharacter: true,
                plan,
                query,
                RagConversationMode.Structured));
        Assert.True(RagVerbatimGuard.ShouldBlockLlm(plan));
    }

    [Fact]
    public void CasualWhatIsArticle4_DropsArticle104_AndFormatsOnlyArticle4()
    {
        var plan = RagQueryPlanner.Plan("刑法4条はなーんだ？", previousUserMessage: null);
        const string article4 =
            "この法律は、日本国外において次に掲げる罪を犯した日本国の公務員に適用する。";
        var hit4 = new RagSearchHit(
            article4,
            "刑法.md",
            "第4条（公務員の国外犯）",
            2,
            "c4");
        var hit104 = new RagSearchHit(
            "他人の刑事事件に関する証拠を隠滅し、偽造し、又は変造した者は、三年以下の拘禁刑又は三十万円以下の罰金に処する。",
            "刑法.md",
            "第104条（証拠隠滅等）",
            40,
            "c104");

        var kept = RagArticleHitFilter.KeepMatching(plan, [hit4, hit104]);
        Assert.Single(kept);
        Assert.Equal("第4条（公務員の国外犯）", kept[0].HeaderText);

        Assert.True(RagVerbatimResponder.TryFormat(plan, kept, japanese: true, out var reply));
        Assert.Contains(article4, reply);
        Assert.Contains("公務員の国外犯", reply);
        Assert.DoesNotContain("証拠隠滅", reply);
        Assert.DoesNotContain("第104条", reply);
    }

    [Fact]
    public void ComparisonQuery_KeepsArticle4And104_Drops114()
    {
        var plan = RagQueryPlanner.Plan("刑法4条と104条の違い", previousUserMessage: null);
        var hit4 = new RagSearchHit("国外で次に掲げる罪", "刑法.md", "第4条（公務員の国外犯）", 2, "c4");
        var hit104 = new RagSearchHit("証拠を隠滅し", "刑法.md", "第104条（証拠隠滅等）", 40, "c104");
        var hit114 = new RagSearchHit("消火を妨害", "刑法.md", "第114条（消火妨害）", 44, "c114");

        var kept = RagArticleHitFilter.KeepMatching(plan, [hit4, hit104, hit114]);
        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, h => h.HeaderText.Contains("第114条", StringComparison.Ordinal));

        Assert.True(RagVerbatimResponder.TryFormat(plan, kept, japanese: true, out var reply));
        Assert.Contains("公務員の国外犯", reply);
        Assert.Contains("証拠隠滅", reply);
        Assert.DoesNotContain("消火妨害", reply);
    }

    [Fact]
    public void NonLegalArticleCue_DoesNotForceFormalLookup()
    {
        Assert.False(RagFormalLegalCue.IsFormalLegalQuery("vectorの3条項"));
    }

    [Fact]
    public void CharacterChitchat_StillSkipsVerbatim()
    {
        var plan = RagQueryPlanner.Plan("今日ごはん何食べた？", previousUserMessage: null);
        Assert.False(
            RagArticleAnswerPolicy.AllowCharacterVerbatim(isCharacter: true, plan, "今日ごはん何食べた？"));
    }
}
