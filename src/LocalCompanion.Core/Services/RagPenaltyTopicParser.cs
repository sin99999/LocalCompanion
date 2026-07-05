using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>「贈賄の罰則」など、罪名トピック付きの罰則質問を解釈する。</summary>
internal static class RagPenaltyTopicParser
{
    private static readonly Regex PenaltyTopicPattern = new(
        @"([ぁ-んァ-ヶー一-龠a-zA-Z]+)の罰則",
        RegexOptions.Compiled);

    public static bool TryGetTopicKeyword(string query, out string keyword)
    {
        keyword = "";
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var match = PenaltyTopicPattern.Match(query);
        if (match.Success)
        {
            var topic = match.Groups[1].Value.Trim();
            if (topic.Length >= 2)
            {
                keyword = topic;
                return true;
            }
        }

        foreach (var direct in DirectKeywords)
        {
            if (query.Contains(direct, StringComparison.Ordinal))
            {
                keyword = direct;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> BuildTextPatterns(string keyword)
    {
        var patterns = new List<string> { keyword };
        if (string.Equals(keyword, "贈賄", StringComparison.Ordinal))
            patterns.Add("賄賂を供与");
        else if (string.Equals(keyword, "受賄", StringComparison.Ordinal))
            patterns.Add("賄賂を収受");
        return patterns;
    }

    private static readonly string[] DirectKeywords =
    [
        "贈賄", "受賄", "殺人", "傷害", "窃盗", "詐欺", "強盗", "放火", "偽造",
    ];
}
