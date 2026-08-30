using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagArticleHitFilterTests
{
    [Fact]
    public void IsMiss_WhenAskedArticle999_AndHitIs93_IsTrue()
    {
        var plan = RagQueryPlanner.Plan(
            "労働基準法第999条の全文を出して。無かったら、無い、ってだけ言って。",
            previousUserMessage: null);
        var hit93 = new RagSearchHit(
            "就業規則で定める基準に達しない労働条件を定める労働契約は、その部分について無効とする。",
            "労働基準法.md",
            "第93条（効力）",
            23,
            "c93");

        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        Assert.Equal(99900, plan.ArticleSortKey);
        Assert.True(RagArticleHitFilter.IsMiss(plan, [hit93]));
        Assert.False(RagVerbatimResponder.TryFormat(plan, [hit93], japanese: true, out _));
        Assert.Equal(
            "登録資料を探しましたが、第999条は見つかりませんでした。",
            RagVerbatimGuard.BuildMissReply(plan, japanese: true));
    }

    [Fact]
    public void TryFormat_MatchingArticle11_ReturnsBody()
    {
        var plan = RagQueryPlanner.Plan("労働基準法第11条の本文を、条文どおり全文だけ出して。", previousUserMessage: null);
        const string body = "この法律で賃金とは、賃金、給料、手当、賞与その他名称の如何を問わず、労働の対償として使用者が労働者に支払うすべてのものをいう。";
        var hit = new RagSearchHit(body, "労働基準法.md", "第11条（定義）", 6, "c11");

        Assert.False(RagArticleHitFilter.IsMiss(plan, [hit]));
        Assert.True(RagVerbatimResponder.TryFormat(plan, [hit], japanese: true, out var reply));
        Assert.Contains(body, reply);
        Assert.Contains("第11条（定義）", reply);
    }

    [Fact]
    public void SkipHybridWhenStructuredEmpty_ForNumberedArticle()
    {
        var plan = RagQueryPlanner.Plan("労働基準法第999条の全文", previousUserMessage: null);
        Assert.True(RagArticleHitFilter.SkipHybridWhenStructuredEmpty(plan));
    }

    [Fact]
    public void KeepMatching_DropsWrongArticle()
    {
        var plan = RagQueryPlanner.Plan("労働基準法第11条", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("無効とする。", "労働基準法.md", "第93条（効力）", 23, "c93"),
            new RagSearchHit("賃金とは", "労働基準法.md", "第11条（定義）", 6, "c11"),
        };

        var kept = RagArticleHitFilter.KeepMatching(plan, hits);
        Assert.Single(kept);
        Assert.Equal("第11条（定義）", kept[0].HeaderText);
    }

    [Fact]
    public void KeepMatching_DropsReadmeWhenBareLegalArticle()
    {
        var plan = RagQueryPlanner.Plan("4条って何？", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("ゲームの第4条です。", "README.md", "第4条", 1, "readme4"),
            new RagSearchHit("日本国外において罪を犯したすべての者に適用する。", "刑法.md", "第4条", 1, "keibo4"),
        };

        var kept = RagArticleHitFilter.KeepMatching(plan, hits);
        Assert.Single(kept);
        Assert.Equal("刑法.md", kept[0].SourceFileName);
        Assert.DoesNotContain(kept, h => h.SourceFileName.Contains("README", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsAmbiguousSources_BareArticle_TwoLaws()
    {
        var plan = RagQueryPlanner.Plan("4条って何？", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("この法律は国外において罪を犯したすべての者に適用する。", "刑法.md", "第4条", 1, "keibo4"),
            new RagSearchHit("能力を有しない。", "民法.md", "第4条", 1, "minpo4"),
        };

        Assert.True(RagArticleHitFilter.IsAmbiguousSources(plan, hits));
        Assert.False(RagVerbatimResponder.TryFormat(plan, hits, japanese: true, out _));
        Assert.False(RagArticleHitFilter.IsMiss(plan, hits));

        var names = RagArticleHitFilter.DistinctMatchingSources(plan, hits);
        var text = RagArticleAmbiguousFormatter.Format(plan, names, AppLanguage.Japanese);
        Assert.Contains("第4条", text, StringComparison.Ordinal);
        Assert.Contains("刑法.md", text, StringComparison.Ordinal);
        Assert.Contains("民法.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("国外において", text, StringComparison.Ordinal);
        Assert.DoesNotContain("見つかりませんでした", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IsAmbiguousSources_NamedStatute_IsFalse()
    {
        var plan = RagQueryPlanner.Plan("刑法4条はなーんだ？", previousUserMessage: null);
        var hits = new[]
        {
            new RagSearchHit("この法律は国外において罪を犯したすべての者に適用する。", "刑法.md", "第4条", 1, "keibo4"),
            new RagSearchHit("能力を有しない。", "民法.md", "第4条", 1, "minpo4"),
        };

        Assert.False(RagArticleHitFilter.IsAmbiguousSources(plan, hits));
    }
}
