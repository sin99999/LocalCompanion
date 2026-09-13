using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>参考資料ブロックの予算。親セクション全文 1 件で数千字になる退行を防ぐ。</summary>
public sealed class RagPromptBudgetTests
{
    private static readonly RagQueryPlan GeneralPlan = RagQueryPlanner.Plan("労働時間の上限は？", null);

    [Fact]
    public void FormatHits_LongParentText_IsCutPerHit_QuoteLineKept()
    {
        var longParent = string.Concat(Enumerable.Repeat("労働時間に関する長い本文。", 400));
        var hit = new RagSearchHit(
            "短い本文", "労働基準法.md", "第32条", 0, "l32",
            ParentText: longParent,
            PenaltyLead: "六箇月以下の懲役又は三十万円以下の罰金に処する。");

        var block = RagPromptBudget.FormatHits([hit], GeneralPlan, hitChars: 300, blockChars: 3600);

        Assert.Contains("【資料記載の罰則文言（引用必須）】六箇月以下", block);
        Assert.Contains("…", block);
        Assert.True(block.Length < 500, $"block was {block.Length}");
    }

    [Fact]
    public void FormatHits_BlockBudget_StopsAddingHitsButKeepsFirst()
    {
        var body = string.Concat(Enumerable.Repeat("本文。", 200));
        var hits = Enumerable.Range(1, 5)
            .Select(i => new RagSearchHit(body, "doc.md", $"見出し{i}", 0, $"c{i}"))
            .ToList();

        var block = RagPromptBudget.FormatHits(hits, GeneralPlan, hitChars: 900, blockChars: 1500);

        Assert.Contains("見出し1", block);
        Assert.Contains("見出し2", block);
        Assert.DoesNotContain("見出し3", block);

        var tiny = RagPromptBudget.FormatHits(hits, GeneralPlan, hitChars: 900, blockChars: 10);
        Assert.Contains("見出し1", tiny);
        Assert.DoesNotContain("見出し2", tiny);
    }

    [Fact]
    public void FormatHits_Advisory_GroupsBySourceFile()
    {
        var plan = RagQueryPlanner.Plan("副業したいけど就業規則と労働基準法でどう？", null);
        Assert.Equal(RagQueryIntent.Advisory, plan.Intent);
        var hits = new[]
        {
            new RagSearchHit("副業の届出", "就業規則.md", "第10条", 0, "a1"),
            new RagSearchHit("労働時間", "労働基準法.md", "第32条", 0, "b1"),
        };

        var block = RagPromptBudget.FormatHits(hits, plan);

        Assert.Contains("■ 就業規則.md", block);
        Assert.Contains("■ 労働基準法.md", block);
        Assert.True(block.IndexOf("■ 就業規則.md", StringComparison.Ordinal) < block.IndexOf("■ 労働基準法.md", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatForPrompt_MaxBodyChars_ZeroMeansNoCut()
    {
        var hit = new RagSearchHit(new string('あ', 2000), "doc.md", "見出し", 0, "c1");
        Assert.Contains(new string('あ', 2000), hit.FormatForPrompt(0));
        Assert.DoesNotContain(new string('あ', 2000), hit.FormatForPrompt(0, 100));
    }
}
