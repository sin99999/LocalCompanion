using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>複数資料の同一条を、先に登録した資料のチャンクだけで埋めない。</summary>
internal static class RagArticleSourceDiversifier
{
    public static IReadOnlyList<RagSearchHit> MergePerSource(
        IReadOnlyList<string> sources,
        Func<string, IReadOnlyList<RagSearchHit>> loadOne)
    {
        if (sources.Count == 0)
            return Array.Empty<RagSearchHit>();
        if (sources.Count == 1)
            return loadOne(sources[0]);

        var merged = new List<RagSearchHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var hit in loadOne(source))
            {
                if (!seen.Add(hit.ChunkId))
                    continue;
                merged.Add(hit);
            }
        }

        return merged;
    }

    public static int PerSourceTopK(int topK, int sourceCount)
    {
        if (sourceCount <= 1)
            return Math.Clamp(Math.Max(topK, 4), 1, 16);
        return Math.Clamp(Math.Max(topK / sourceCount, 2), 1, 8);
    }

    public static IReadOnlyList<RagSearchHit> KeepAtLeastOnePerSource(
        IReadOnlyList<RagSearchHit> hits,
        int topK)
    {
        if (hits.Count == 0)
            return hits;

        var groups = hits
            .GroupBy(h => h.SourceFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count <= 1)
            return hits.Take(Math.Max(topK, 1)).ToList();

        var per = Math.Max(1, topK / groups.Count);
        var picked = new List<RagSearchHit>();
        foreach (var group in groups)
            picked.AddRange(group.Take(per));
        return picked;
    }
}
