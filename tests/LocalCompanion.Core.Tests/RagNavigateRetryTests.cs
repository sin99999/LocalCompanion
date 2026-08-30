using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagNavigateRetryTests
{
    [Fact]
    public void Rank_PutsMatchingShelfFirst()
    {
        var shelves = new[]
        {
            new RagShelf(@"docs\notes.md", "雑談", "メモ", 10),
            new RagShelf(@"docs\刑法.md", "第2編 > 第235条", "第235条（窃盗）", 3),
        };

        var ranked = RagShelfCatalog.Rank("刑法の窃盗", shelves);
        Assert.NotEmpty(ranked);
        Assert.Contains("刑法", ranked[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepRelevant_DropsWrongShelfWhenNeedleMisses()
    {
        var hits = new[]
        {
            new RagSearchHit(
                "猫の飼い方",
                @"docs\pets.md",
                "猫",
                1,
                "c1",
                SectionPath: "動物 > 猫"),
        };

        var kept = RagNavigateRetry.KeepRelevant(hits, "刑法の窃盗", "第235条");
        Assert.Empty(kept);
    }

    [Fact]
    public void KeepRelevant_KeepsMatchingHeading()
    {
        var hits = new[]
        {
            new RagSearchHit(
                "他人の財物を窃取した者は、窃盗の罪とする。",
                @"docs\刑法.md",
                "第235条（窃盗）",
                1,
                "c1",
                SectionPath: "第2編 > 第235条"),
        };

        var kept = RagNavigateRetry.KeepRelevant(hits, "窃盗の条文", null);
        Assert.Single(kept);
    }

    [Fact]
    public void BuildScopes_WidensFromHintToAll()
    {
        var hinted = new[] { @"docs\notes.md" };
        var enabled = new[] { @"docs\notes.md", @"docs\刑法.md" };
        var shelves = new[]
        {
            new RagShelf(@"docs\刑法.md", "第235条", "第235条", 2),
        };

        var scopes = RagNavigateRetry.BuildScopes(hinted, enabled, shelves, "第235条");
        Assert.True(scopes.Count >= 2);
        Assert.True(scopes.Count <= RagNavigateRetry.MaxHybridPasses);
        Assert.Contains(scopes, s => s.Sources.Count == enabled.Length || s.Sources.Count == 1);
    }
}
