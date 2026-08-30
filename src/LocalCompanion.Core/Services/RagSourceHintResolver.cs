using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>登録済みソース名・URL からクエリに合う資料ヒントを動的に解決する。</summary>
internal static class RagSourceHintResolver
{
    private static readonly Regex TokenSplit = new(
        @"[\s_\-./\\]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static RagQueryPlan EnrichPlan(RagQueryPlan plan, IReadOnlyList<string> enabledSources)
    {
        if (enabledSources.Count == 0)
            return plan;

        var primary = ResolvePrimary(plan.EffectiveQuery, enabledSources);
        if (string.IsNullOrWhiteSpace(primary))
            return plan;

        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.SourceHint))
            hints.Add(plan.SourceHint);
        if (plan.SourceHints is { Count: > 0 })
        {
            foreach (var hint in plan.SourceHints)
            {
                if (!hints.Contains(hint, StringComparer.OrdinalIgnoreCase))
                    hints.Add(hint);
            }
        }

        if (!hints.Contains(primary, StringComparer.OrdinalIgnoreCase))
            hints.Insert(0, primary);

        return plan with { SourceHint = primary, SourceHints = hints };
    }

    public static IReadOnlyList<string> FilterEnabled(RagQueryPlan plan, IReadOnlyList<string> enabledSources)
    {
        if (enabledSources.Count == 0)
            return enabledSources;

        if (plan.Intent == RagQueryIntent.Article && plan.SourceHints is { Count: > 1 })
        {
            var union = UnionByHints(enabledSources, plan.SourceHints);
            if (union.Count > 0)
                return union;
        }

        if (string.IsNullOrWhiteSpace(plan.SourceHint))
            return enabledSources;

        var one = UnionByHints(enabledSources, [plan.SourceHint]);
        return one.Count > 0 ? one : enabledSources;
    }

    private static List<string> UnionByHints(IReadOnlyList<string> enabledSources, IReadOnlyList<string> hints)
    {
        var union = new List<string>();
        foreach (var hint in hints)
        {
            if (string.IsNullOrWhiteSpace(hint))
                continue;
            foreach (var source in enabledSources)
            {
                if (!MatchesHint(source, hint))
                    continue;
                if (union.Exists(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase)))
                    continue;
                union.Add(source);
            }
        }

        return union;
    }

    internal static bool MatchesHint(string source, string sourceHint)
    {
        if (string.IsNullOrWhiteSpace(sourceHint))
            return true;
        if (source.Contains(sourceHint, StringComparison.OrdinalIgnoreCase))
            return true;
        var fileName = Path.GetFileName(source);
        if (fileName.Contains(sourceHint, StringComparison.OrdinalIgnoreCase))
            return true;
        var label = RagSourceLabel.Format(source);
        return label.Contains(sourceHint, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolvePrimary(string query, IReadOnlyList<string> enabledSources)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var staticHint = RagSourceHintCatalog.ExtractPrimaryHint(query);
        if (!string.IsNullOrWhiteSpace(staticHint))
            return staticHint;

        var bestHint = "";
        var bestScore = 0;
        foreach (var source in enabledSources)
        {
            var (hint, score) = ScoreSource(query, source);
            if (score > bestScore)
            {
                bestScore = score;
                bestHint = hint;
            }
        }

        return bestScore >= 3 ? bestHint : null;
    }

    private static (string Hint, int Score) ScoreSource(string query, string source)
    {
        var q = query.ToLowerInvariant();
        var label = RagSourceLabel.Format(source).ToLowerInvariant();
        var fileName = Path.GetFileName(label).ToLowerInvariant();
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        var score = 0;
        var hint = nameWithoutExt;

        if (nameWithoutExt.Length >= 3 && q.Contains(nameWithoutExt, StringComparison.Ordinal))
            score += 12;

        if (fileName.Length >= 3 && q.Contains(fileName, StringComparison.Ordinal))
            score += 10;

        foreach (var token in TokenSplit.Split(nameWithoutExt))
        {
            if (token.Length < 3)
                continue;

            if (q.Contains(token, StringComparison.Ordinal))
            {
                score += 4;
                hint = token;
            }
        }

        if (source.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var token in TokenSplit.Split(label))
            {
                if (token.Length < 4)
                    continue;

                if (q.Contains(token, StringComparison.Ordinal))
                {
                    score += 5;
                    hint = token;
                }
            }
        }

        return (hint, score);
    }
}

internal static class RagSourceLabel
{
    public static string Format(string source) =>
        source.StartsWith("url:", StringComparison.OrdinalIgnoreCase)
            ? source["url:".Length..].Trim()
            : source;
}
