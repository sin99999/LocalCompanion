using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>「Xとは」「Xの意味」など定義質問から用語を抽出する。</summary>
internal static class RagDefinitionQueryParser
{
    private static readonly Regex ToHa = new(@"^(.+?)とは[？?]?$", RegexOptions.Compiled);
    private static readonly Regex Meaning = new(@"^(.+?)の意味(?:は|を)?[？?]?$", RegexOptions.Compiled);
    private static readonly Regex Definition = new(@"^(.+?)の定義(?:は|を)?[？?]?$", RegexOptions.Compiled);
    private static readonly Regex WhatIs = new(@"^what\s+is\s+(.+?)\??$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Teaches = new(@"^(.+?)って何[？?]?$", RegexOptions.Compiled);

    public static bool TryGetTerm(string query, out string term)
    {
        term = "";
        var q = query.Trim();
        if (q.Length < 3)
            return false;

        foreach (var pattern in new[] { ToHa, Meaning, Definition, Teaches, WhatIs })
        {
            var match = pattern.Match(q);
            if (!match.Success)
                continue;

            term = match.Groups[1].Value.Trim();
            if (term.Length is >= 1 and <= 80 && !ContainsLegalCue(term))
                return true;
        }

        return false;
    }

    private static bool ContainsLegalCue(string term) =>
        term.Contains('条', StringComparison.Ordinal)
        || term.Contains("罰則", StringComparison.Ordinal)
        || term.Contains("条文", StringComparison.Ordinal);
}
