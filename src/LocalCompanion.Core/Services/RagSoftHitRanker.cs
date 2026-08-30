using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// SoftTopic 向けの軽い再順位付け（交差エンコーダではない）。
/// クエリ語の重なりが多いチャンクを前に出し、ゼロ重なりは落とす。
/// </summary>
internal static class RagSoftHitRanker
{
    public const int DefaultTake = 3;

    public static IReadOnlyList<RagSearchHit> OrderByNeedleOverlap(
        IReadOnlyList<RagSearchHit> hits,
        string query,
        int take = DefaultTake)
    {
        if (hits.Count == 0)
            return hits;

        var limit = Math.Clamp(take, 1, 16);
        var needles = RagConversationGate.ExtractNeedles(query);
        if (needles.Count == 0)
            return hits.Take(limit).ToList();

        return hits
            .Select(h => (Hit: h, Score: CountNeedleHits(h, needles)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hit.SourceFileName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Hit)
            .ToList();
    }

    internal static int CountNeedleHits(RagSearchHit hit, IReadOnlyList<string> needles)
    {
        var hay = string.Concat(
            hit.SourceFileName, "\n",
            hit.HeaderText, "\n",
            hit.SectionPath, "\n",
            hit.PromptText, "\n",
            hit.DefinitionLead, "\n",
            hit.PenaltyLead);
        var score = 0;
        foreach (var needle in needles)
        {
            if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase))
                score++;
        }

        return score;
    }
}
