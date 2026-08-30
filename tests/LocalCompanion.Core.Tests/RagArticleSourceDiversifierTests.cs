using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagArticleSourceDiversifierTests
{
    [Fact]
    public void MergePerSource_DoesNotLetFirstLawFillTheWholeList()
    {
        var sources = new[] { @"C:\law\刑法.md", @"C:\law\民法.md" };
        var merged = RagArticleSourceDiversifier.MergePerSource(
            sources,
            src =>
            {
                if (src.Contains("刑法", StringComparison.Ordinal))
                {
                    return Enumerable.Range(0, 8)
                        .Select(i => new RagSearchHit(
                            $"刑法チャンク{i}",
                            src,
                            "第4条",
                            1,
                            $"keibo-{i}"))
                        .ToList();
                }

                return
                [
                    new RagSearchHit("民法本文", src, "第4条", 1, "minpo-0"),
                ];
            });

        Assert.Contains(merged, h => h.Source.Contains("刑法", StringComparison.Ordinal));
        Assert.Contains(merged, h => h.Source.Contains("民法", StringComparison.Ordinal));
        Assert.Contains(merged, h => h.Text.Contains("民法本文", StringComparison.Ordinal));
    }

    [Fact]
    public void PerSourceTopK_MultiSource_IsSmallerThanSingleLimit()
    {
        Assert.Equal(4, RagArticleSourceDiversifier.PerSourceTopK(4, 1));
        Assert.True(RagArticleSourceDiversifier.PerSourceTopK(4, 2) <= 8);
        Assert.True(RagArticleSourceDiversifier.PerSourceTopK(4, 2) >= 2);
    }

    [Fact]
    public void KeepAtLeastOnePerSource_DoesNotDropSecondLawWhenFirstFillsTopK()
    {
        var hits = Enumerable.Range(0, 4)
            .Select(i => new RagSearchHit($"刑法{i}", "刑法.md", "第4条", 1, $"k{i}"))
            .Concat([new RagSearchHit("民法本文", "民法.md", "第4条", 1, "m0")])
            .ToList();

        var kept = RagArticleSourceDiversifier.KeepAtLeastOnePerSource(hits, topK: 4);
        Assert.Contains(kept, h => h.SourceFileName == "刑法.md");
        Assert.Contains(kept, h => h.Text.Contains("民法本文", StringComparison.Ordinal));
    }
}
