namespace LocalCompanion.Services;

/// <summary>FTS5 とベクトル検索の Reciprocal Rank Fusion。</summary>
internal static class RagHybridSearch
{
    public static IReadOnlyList<long> FuseRrf(
        IReadOnlyList<long> ftsIds,
        IReadOnlyList<long> vectorIds,
        int topK,
        int rrfK,
        double weightFts,
        double weightVec)
    {
        if (topK <= 0)
            return Array.Empty<long>();

        var scores = new Dictionary<long, double>();
        Accumulate(scores, ftsIds, weightFts, rrfK);
        Accumulate(scores, vectorIds, weightVec, rrfK);

        if (scores.Count == 0)
            return Array.Empty<long>();

        return scores
            .OrderByDescending(static x => x.Value)
            .ThenBy(static x => x.Key)
            .Take(topK)
            .Select(static x => x.Key)
            .ToList();
    }

    public static (double Fts, double Vec) ResolveWeights(string query, double baseFts, double baseVec)
    {
        if (RagArticleQueryParser.TryGetArticleNumber(query, out _))
            return (0.65, 0.35);

        if (query.Contains('条', StringComparison.Ordinal)
            || query.Contains("Article", StringComparison.OrdinalIgnoreCase)
            || query.Any(char.IsDigit))
        {
            return query.Contains('罰', StringComparison.Ordinal) ? (0.65, 0.35) : (0.55, 0.45);
        }

        return (baseFts, baseVec);
    }

    private static void Accumulate(
        Dictionary<long, double> scores,
        IReadOnlyList<long> ids,
        double weight,
        int rrfK)
    {
        if (weight <= 0 || ids.Count == 0)
            return;

        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var increment = weight / (rrfK + i + 1);
            scores[id] = scores.GetValueOrDefault(id) + increment;
        }
    }
}
