using System.Text.RegularExpressions;

using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>「第8条」「8条全文」「最後の条文」など、法令条文クエリを解釈する。</summary>
internal static class RagArticleQueryParser
{
    private static readonly Regex ArticlePattern = new(
        @"(?:第\s*)?([0-9０-９]{1,3})\s*条",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HeaderArticlePattern = new(
        @"^第\s*([0-9０-９]{1,3})\s*条(?:の\s*([0-9０-９]{1,2}))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WholeLawMarker = new(
        @"全体|全条|法律全体|法全体|この法|当該法",
        RegexOptions.Compiled);

    private static readonly Regex LastBoundaryPattern = new(
        @"(?:最後|最終|末尾)(?:の)?(?:条文|条)|(?:何条|どの条).*(?:最後|終[わり]|まで)|(?:最後|終[わり]).*第\s*何\s*条",
        RegexOptions.Compiled);

    private static readonly Regex FirstBoundaryPattern = new(
        @"(?:最初|先頭|第一)(?:の)?(?:条文|条)|(?:何条|どの条).*(?:最初|始[まり])",
        RegexOptions.Compiled);

    public static bool TryGetArticleNumber(string query, out int articleNumber)
    {
        articleNumber = 0;
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var match = ArticlePattern.Match(query);
        if (!match.Success)
            return false;

        var digits = NormalizeDigits(match.Groups[1].Value);
        return int.TryParse(digits, out articleNumber) && articleNumber > 0;
    }

    public static bool TryGetBoundaryIntent(string query, out RagArticleBoundaryIntent intent)
    {
        intent = RagArticleBoundaryIntent.Last;
        if (string.IsNullOrWhiteSpace(query))
            return false;

        if (IsWithinArticleBoundaryQuery(query))
            return false;

        if (LastBoundaryPattern.IsMatch(query))
        {
            intent = RagArticleBoundaryIntent.Last;
            return true;
        }

        if (FirstBoundaryPattern.IsMatch(query))
        {
            intent = RagArticleBoundaryIntent.First;
            return true;
        }

        return false;
    }

    public static bool TryParseArticleSortKey(string headerText, out long sortKey)
    {
        sortKey = 0;
        if (string.IsNullOrWhiteSpace(headerText))
            return false;

        var match = HeaderArticlePattern.Match(StripMarkdownHeadingPrefix(headerText));
        if (!match.Success)
            return false;

        var mainDigits = NormalizeDigits(match.Groups[1].Value);
        if (!int.TryParse(mainDigits, out var main) || main <= 0)
            return false;

        var sub = 0;
        if (match.Groups[2].Success)
        {
            var subDigits = NormalizeDigits(match.Groups[2].Value);
            if (!int.TryParse(subDigits, out sub) || sub < 0)
                return false;
        }

        sortKey = main * 100L + sub;
        return true;
    }

    public static string FormatArticleLabel(long sortKey)
    {
        var main = sortKey / 100;
        var sub = sortKey % 100;
        return sub == 0 ? $"第{main}条" : $"第{main}条の{sub}";
    }

    public static string? ExtractSourceHint(string query) =>
        RagSourceHintCatalog.ExtractPrimaryHint(query);

    /// <summary>header_text の前方一致用（半角・全角数字の両方）。</summary>
    public static IReadOnlyList<string> BuildHeaderPrefixes(int articleNumber)
    {
        var half = articleNumber.ToString();
        var full = ToFullWidthDigits(half);
        var prefixes = new List<string>(2) { $"第{half}条" };
        if (!string.Equals(half, full, StringComparison.Ordinal))
            prefixes.Add($"第{full}条");
        return prefixes;
    }

    private static bool IsWithinArticleBoundaryQuery(string query)
    {
        if (!ArticlePattern.IsMatch(query))
            return false;

        if (WholeLawMarker.IsMatch(query))
            return false;

        return query.Contains("の最後", StringComparison.Ordinal)
            || query.Contains("の末尾", StringComparison.Ordinal)
            || query.Contains("の終わり", StringComparison.Ordinal)
            || query.Contains("の中で", StringComparison.Ordinal);
    }

    /// <summary>DB や本文先頭行に残った <c>#### 第N条</c> 形式を正規化する。</summary>
    internal static string StripMarkdownHeadingPrefix(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('#'))
            return trimmed;

        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        return level is >= 1 and <= 6 ? trimmed[level..].Trim() : trimmed;
    }

    private static string NormalizeDigits(string value) =>
        value.Replace('０', '0').Replace('１', '1').Replace('２', '2').Replace('３', '3')
            .Replace('４', '4').Replace('５', '5').Replace('６', '6').Replace('７', '7')
            .Replace('８', '8').Replace('９', '9');

    private static string ToFullWidthDigits(string halfWidth) =>
        string.Concat(halfWidth.Select(static c => c switch
        {
            '0' => '０',
            '1' => '１',
            '2' => '２',
            '3' => '３',
            '4' => '４',
            '5' => '５',
            '6' => '６',
            '7' => '７',
            '8' => '８',
            '9' => '９',
            _ => c,
        }));
}
