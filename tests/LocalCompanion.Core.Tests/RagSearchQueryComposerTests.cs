using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSearchQueryComposerTests
{
    [Fact]
    public void Compose_FollowUpQuestion_MergesPreviousUserMessage()
    {
        var query = RagSearchQueryComposer.Compose(
            "RAGを参照して正確に教えて？",
            "贈賄の罰則は？");

        Assert.Contains("贈賄", query);
        Assert.Contains("RAG", query);
    }

    [Fact]
    public void Compose_SubstantiveQuestion_KeepsCurrentOnly()
    {
        var query = RagSearchQueryComposer.Compose(
            "刑法の第1条全文を教えて",
            "贈賄の罰則は？");

        Assert.Equal("刑法の第1条全文を教えて", query);
    }

    [Fact]
    public void Compose_BareArticleFollowUp_MergesLegalPrevious()
    {
        var query = RagSearchQueryComposer.Compose("4条って何？", "刑法の話をしよう");
        Assert.Contains("刑法", query);
        Assert.Contains("4条", query);
    }

    [Fact]
    public void Compose_ReadmeArticle_DoesNotMergeLegalPrevious()
    {
        var query = RagSearchQueryComposer.Compose("READMEの3条について", "刑法4条はなーんだ？");
        Assert.Equal("READMEの3条について", query);
    }

    [Fact]
    public void Compose_SoftTopicAfterArticle999_DoesNotMergeArticle()
    {
        var query = RagSearchQueryComposer.Compose(
            "残業の法律ってどうなってる？",
            "刑法第999条の全文を出して。無かったら、無い、ってだけ言って。");

        Assert.Equal("残業の法律ってどうなってる？", query);
        Assert.DoesNotContain("999", query);
    }

    [Fact]
    public void Compose_RagFollowUpAfterArticle999_DoesNotMergeArticle()
    {
        var query = RagSearchQueryComposer.Compose(
            "RAGを見て残業の法律ってどうなってる？",
            "労働基準法第999条の全文を出して。無かったら、無い、ってだけ言って。");

        Assert.Equal("RAGを見て残業の法律ってどうなってる？", query);
        Assert.DoesNotContain("999", query);
    }

    [Fact]
    public void ShouldBlockArticleHistoryMerge_PreviousArticle_CurrentSoft_True()
    {
        Assert.True(RagSearchQueryComposer.ShouldBlockArticleHistoryMerge(
            "残業の法律ってどうなってる？",
            "刑法第999条の全文"));
    }

    [Fact]
    public void Compose_MurderAfterShoplifting_DoesNotMergePriorCrime()
    {
        var query = RagSearchQueryComposer.Compose(
            "人を仕方なく殺す(ナイフ)場合は捕まる？",
            "万引きしたら捕まる？");

        Assert.Equal("人を仕方なく殺す(ナイフ)場合は捕まる？", query);
        Assert.DoesNotContain("万引き", query);
    }

    [Fact]
    public void Compose_AbductionAfterTheft_DoesNotMergePriorCrime()
    {
        var query = RagSearchQueryComposer.Compose(
            "未成年略取とか無かった？",
            "万引きしたら捕まる？");

        Assert.Equal("未成年略取とか無かった？", query);
        Assert.DoesNotContain("万引き", query);
    }
}
