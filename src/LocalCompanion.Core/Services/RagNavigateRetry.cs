using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>目次で棚を決めて検索し、外れなら範囲を広げてやり直す。</summary>
internal sealed record RagSearchScope(IReadOnlyList<string> Sources, string? SectionNeedle);

internal static class RagNavigateRetry
{
    public const int MaxHybridPasses = 3;

    public static IReadOnlyList<RagSearchScope> BuildScopes(
        IReadOnlyList<string> hintedSources,
        IReadOnlyList<string> enabledSources,
        IReadOnlyList<RagShelf> rankedShelves,
        string? sectionNeedle)
    {
        var scopes = new List<RagSearchScope>();
        var hintedNarrow = hintedSources.Count > 0 && hintedSources.Count < enabledSources.Count;
        var catalogSources = RagShelfCatalog.DistinctSources(rankedShelves);

        if (hintedNarrow && !string.IsNullOrWhiteSpace(sectionNeedle))
            Add(scopes, hintedSources, sectionNeedle);

        if (hintedNarrow)
            Add(scopes, hintedSources, null);

        if (catalogSources.Count > 0
            && catalogSources.Count < enabledSources.Count
            && !SameSourceSet(catalogSources, hintedSources))
            Add(scopes, catalogSources, null);

        Add(scopes, enabledSources, null);

        if (scopes.Count > MaxHybridPasses)
            return scopes.Take(MaxHybridPasses).ToList();

        return scopes;
    }

    public static IReadOnlyList<RagSearchHit> KeepRelevant(
        IReadOnlyList<RagSearchHit> hits,
        string query,
        string? sectionNeedle)
    {
        if (hits.Count == 0)
            return hits;

        IReadOnlyList<RagSearchHit> scoped = hits;
        if (!string.IsNullOrWhiteSpace(sectionNeedle))
        {
            var onShelf = hits
                .Where(h => ShelfHaystack(h).Contains(sectionNeedle, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (onShelf.Count == 0)
                return Array.Empty<RagSearchHit>();
            scoped = onShelf;
        }

        var needles = RagConversationGate.ExtractNeedles(query);
        if (needles.Count == 0)
            return scoped;

        var kept = RagConversationGate.FilterWeakHits(scoped, query);
        return kept;
    }

    private static void Add(
        List<RagSearchScope> scopes,
        IReadOnlyList<string> sources,
        string? sectionNeedle)
    {
        if (sources.Count == 0)
            return;

        foreach (var existing in scopes)
        {
            if (SameSourceSet(existing.Sources, sources)
                && string.Equals(existing.SectionNeedle, sectionNeedle, StringComparison.OrdinalIgnoreCase))
                return;
        }

        scopes.Add(new RagSearchScope(sources, sectionNeedle));
    }

    private static bool SameSourceSet(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var item in a)
        {
            if (!b.Contains(item, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string ShelfHaystack(RagSearchHit hit) =>
        string.Concat(
            hit.SourceFileName, "\n",
            hit.HeaderText, "\n",
            hit.SectionPath, "\n",
            hit.ParentText);
}
