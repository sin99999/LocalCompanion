using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>条番号つき質問のヒットが、聞かれた条と一致するかだけ見る。</summary>
internal static class RagArticleHitFilter
{
    public static IReadOnlyList<long> RequestedKeys(RagQueryPlan plan)
    {
        if (plan.ArticleBindings is { Count: > 0 })
            return plan.ArticleBindings.Select(static b => b.SortKey).Distinct().ToList();
        if (plan.ArticleSortKeys is { Count: > 0 })
            return plan.ArticleSortKeys;
        if (plan.ArticleSortKey is > 0)
            return [plan.ArticleSortKey.Value];
        return [];
    }

    public static bool MatchesRequested(RagQueryPlan plan, RagSearchHit hit)
    {
        if (plan.ArticleBindings is { Count: > 0 })
        {
            return plan.ArticleBindings.Any(b =>
                hit.ArticleSortKey == b.SortKey
                && RagSourceHintResolver.MatchesHint(hit.Source, b.Hint));
        }

        var keys = RequestedKeys(plan);
        if (keys.Count == 0)
            return true;
        return keys.Contains(hit.ArticleSortKey);
    }

    public static IReadOnlyList<RagSearchHit> KeepMatching(
        RagQueryPlan plan,
        IReadOnlyList<RagSearchHit> hits)
    {
        if (plan.Intent != RagQueryIntent.Article || RequestedKeys(plan).Count == 0)
            return hits;

        var kept = hits.Where(h => MatchesRequested(plan, h)).ToList();
        if (!ShouldDropNonLegalSources(plan))
            return kept;

        return kept
            .Where(h => !RagLegalQueryContext.LooksLikeNamedNonLegalDoc(h.SourceFileName))
            .ToList();
    }

    private static bool ShouldDropNonLegalSources(RagQueryPlan plan)
    {
        var query = plan.EffectiveQuery;
        if (RagLegalQueryContext.LooksLikeNamedNonLegalDoc(query))
            return false;
        return RagLegalQueryContext.LooksLikeLegalArticleQuery(query, plan.SourceHint);
    }

    public static bool IsMiss(RagQueryPlan plan, IReadOnlyList<RagSearchHit> hits) =>
        plan.Intent == RagQueryIntent.Article
        && RequestedKeys(plan).Count > 0
        && !hits.Any(h => MatchesRequested(plan, h));

    public static IReadOnlyList<string> DistinctMatchingSources(
        RagQueryPlan plan,
        IReadOnlyList<RagSearchHit> hits)
    {
        return KeepMatching(plan, hits)
            .Select(h => h.SourceFileName)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsAmbiguousSources(RagQueryPlan plan, IReadOnlyList<RagSearchHit> hits)
    {
        if (plan.Intent != RagQueryIntent.Article || RequestedKeys(plan).Count == 0)
            return false;
        if (!string.IsNullOrWhiteSpace(plan.SourceHint))
            return false;
        if (!string.IsNullOrWhiteSpace(RagSourceHintCatalog.ExtractPrimaryHint(plan.EffectiveQuery)))
            return false;
        return DistinctMatchingSources(plan, hits).Count >= 2;
    }

    public static bool WantsAllNamedSources(RagQueryPlan plan) =>
        plan.Intent == RagQueryIntent.Article
        && plan.SourceHints is { Count: > 1 };

    public static bool SkipHybridWhenStructuredEmpty(RagQueryPlan plan) =>
        plan.Intent == RagQueryIntent.Article && RequestedKeys(plan).Count > 0;
}
