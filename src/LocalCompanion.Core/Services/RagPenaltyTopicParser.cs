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
            var topic = NormalizeTopicKeyword(match.Groups[1].Value.Trim());
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
        var normalized = NormalizeTopicKeyword(keyword);
        var patterns = new List<string> { normalized };
        if (!string.Equals(normalized, keyword, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(keyword))
            patterns.Add(keyword);
        if (string.Equals(normalized, "贈賄", StringComparison.Ordinal))
            patterns.Add("賄賂を供与");
        else if (string.Equals(normalized, "受賄", StringComparison.Ordinal))
            patterns.Add("賄賂を収受");
        return patterns;
    }

    /// <summary>「住居侵入罪」→「住居侵入」など、見出し照合用に末尾の「罪」を外す。</summary>
    internal static string NormalizeTopicKeyword(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return "";
        var t = topic.Trim();
        if (t.Length >= 3 && t.EndsWith("罪", StringComparison.Ordinal))
            return t[..^1];
        return t;
    }

    private static readonly string[] DirectKeywords =
    [
        "贈賄", "受賄", "殺人", "傷害", "窃盗", "詐欺", "強盗", "放火", "偽造", "国外犯",
    ];
}
