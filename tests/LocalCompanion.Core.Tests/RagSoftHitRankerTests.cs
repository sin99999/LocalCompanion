using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSoftHitRankerTests
{
    [Fact]
    public void OrderByNeedleOverlap_PutsHigherOverlapFirst_AndDropsZero()
    {
        var hits = new[]
        {
            new RagSearchHit("無関係な本文", "notes.md", "メモ", 1, "n1"),
            new RagSearchHit("四十時間と残業の上限", "労働基準法.md", "第32条", 1, "l1"),
            new RagSearchHit("残業だけ触れるメモ", "memo.md", "メモ2", 1, "m1"),
        };

        var ranked = RagSoftHitRanker.OrderByNeedleOverlap(hits, "残業 四十時間 法律", take: 3);
        Assert.Equal(2, ranked.Count);
        Assert.Equal("労働基準法.md", ranked[0].Source);
        Assert.DoesNotContain(ranked, h => h.Source == "notes.md");
    }
}
