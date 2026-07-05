using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>資料ファイル名マッチ用のソースヒント。</summary>
internal static class RagSourceHintCatalog
{
    private static readonly (Regex Pattern, string Hint)[] Hints =
    [
        (new Regex(@"労働基準法|労基法", RegexOptions.Compiled), "労働基準法"),
        (new Regex(@"刑法", RegexOptions.Compiled), "刑法"),
        (new Regex(@"就業規則|社内規定|就規|雇用規則", RegexOptions.Compiled), "就業規則"),
        (new Regex(@"税法|所得税|法人税|消費税|節税|雑所得|法人化", RegexOptions.Compiled), "税法"),
        (new Regex(@"民法", RegexOptions.Compiled), "民法"),
        (new Regex(@"会社法", RegexOptions.Compiled), "会社法"),
    ];

    public static IReadOnlyList<string> ExtractAllHints(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var (pattern, hint) in Hints)
        {
            if (pattern.IsMatch(query) && !list.Contains(hint, StringComparer.Ordinal))
                list.Add(hint);
        }

        return list;
    }

    public static string? ExtractPrimaryHint(string query)
    {
        var hints = ExtractAllHints(query);
        return hints.Count > 0 ? hints[0] : null;
    }
}
