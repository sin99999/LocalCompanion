using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>ソースパスと本文サンプルから ingest プロファイルを推定する。</summary>
internal static class RagDocumentProfileDetector
{
    private static readonly Regex ArticleHeader = new(@"第\s*\d+\s*条", RegexOptions.Compiled);
    private static readonly Regex PenaltyWord = new(@"(懲役|罰金|禁錮|拘留|科料)", RegexOptions.Compiled);

    public static RagDocumentKind Detect(string source, string text)
    {
        var fileName = Path.GetFileName(source);
        var lowerName = fileName.ToLowerInvariant();

        if (ContainsAny(lowerName, "用語集", "glossary", "terms", "dictionary", "辞典"))
            return RagDocumentKind.Glossary;

        if (ContainsAny(lowerName, "刑法", "労基", "労働基準", "民法", "憲法", "法律", "law", "legal"))
            return RagDocumentKind.Legal;

        var sample = text.Length > 12000 ? text[..12000] : text;
        var articleCount = ArticleHeader.Matches(sample).Count;
        var penaltyCount = PenaltyWord.Matches(sample).Count;

        if (articleCount >= 3 || (articleCount >= 1 && penaltyCount >= 2))
            return RagDocumentKind.Legal;

        if (IsGlossaryShape(sample))
            return RagDocumentKind.Glossary;

        return RagDocumentKind.General;
    }

    public static string ToStorageValue(RagDocumentKind kind) => kind switch
    {
        RagDocumentKind.Legal => "legal",
        RagDocumentKind.Glossary => "glossary",
        _ => "general",
    };

    private static bool IsGlossaryShape(string sample)
    {
        var lines = sample.Split('\n');
        var shortHeaders = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                var title = line.TrimStart('#').Trim();
                if (title.Length is > 0 and <= 40)
                    shortHeaders++;
            }
        }

        return shortHeaders >= 5;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
