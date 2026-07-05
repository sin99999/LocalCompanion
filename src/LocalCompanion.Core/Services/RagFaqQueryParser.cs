using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>FAQ 形式の質問文から検索キーを抽出する。</summary>
internal static class RagFaqQueryParser
{
    private static readonly Regex QuestionEnd = new(@"^(.{4,120})[？?]$", RegexOptions.Compiled);
    private static readonly Regex HowToEn = new(@"^how\s+(?:do\s+i|to)\s+(.+?)\??$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] FaqCues =
    [
        "方法", "手順", "やり方", "どうすれば", "どうやって", "できますか", "可能ですか",
        "how to", "how do",
    ];

    public static bool TryGetQuestion(string query, out string normalizedKey)
    {
        normalizedKey = "";
        var q = query.Trim();
        if (q.Length < 4)
            return false;

        if (RagDefinitionQueryParser.TryGetTerm(q, out _))
            return false;

        if (q.Contains("FAQ", StringComparison.OrdinalIgnoreCase)
            || q.Contains("よくある質問", StringComparison.Ordinal))
        {
            normalizedKey = RagEntryKeyNormalizer.Normalize(q);
            return normalizedKey.Length >= 2;
        }

        var how = HowToEn.Match(q);
        if (how.Success)
        {
            normalizedKey = RagEntryKeyNormalizer.Normalize(how.Groups[1].Value.Trim());
            return normalizedKey.Length >= 3;
        }

        var end = QuestionEnd.Match(q);
        if (end.Success && ContainsFaqCue(q))
        {
            normalizedKey = RagEntryKeyNormalizer.Normalize(end.Groups[1].Value.Trim());
            return normalizedKey.Length >= 3;
        }

        if (ContainsFaqCue(q) && q.Length is >= 6 and <= 120)
        {
            normalizedKey = RagEntryKeyNormalizer.Normalize(q.TrimEnd('？', '?'));
            return normalizedKey.Length >= 3;
        }

        return false;
    }

    private static bool ContainsFaqCue(string query)
    {
        foreach (var cue in FaqCues)
        {
            if (query.Contains(cue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
