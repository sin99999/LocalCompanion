using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>複数資料を横断する相談・助言クエリを検出する。</summary>
internal static class RagAdvisoryQueryParser
{
    private static readonly Regex MoneyCue = new(
        @"\d+\s*億|\d+\s*万|100\s*億|十億|百億",
        RegexOptions.Compiled);

    private static readonly string[] AdvisoryKeywords =
    [
        "相談", "副業", "売却", "買収", "買い取", "M&A", "辞め", "退職", "就業規則",
        "社内規定", "規定", "禁止", "違反", "税法", "節税", "法人化", "雑所得",
        "ビッグテック", "big tech", "買いたい", "オファー", "提案",
    ];

    public static bool TryDetect(string query, out IReadOnlyList<string> sourceHints)
    {
        sourceHints = RagSourceHintCatalog.ExtractAllHints(query);
        if (sourceHints.Count >= 2)
            return true;

        var hasAdvisoryKeyword = AdvisoryKeywords.Any(k =>
            query.Contains(k, StringComparison.OrdinalIgnoreCase));
        var hasMoney = MoneyCue.IsMatch(query);

        if (sourceHints.Count >= 1 && (hasAdvisoryKeyword || hasMoney))
            return true;

        if (hasAdvisoryKeyword && hasMoney)
        {
            sourceHints = sourceHints.Count > 0
                ? sourceHints
                : new[] { "就業規則", "税法" };
            return true;
        }

        return false;
    }
}
