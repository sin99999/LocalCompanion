namespace LocalCompanion.Services;

/// <summary>取り込み時に条文番号・罰則文言などを構造化する。</summary>
internal static class LegalFieldExtractor
{
    public static (int Main, int Sub, long SortKey) ParseArticle(string headerText, string? bodyText = null)
    {
        if (TryParseArticleFromText(headerText, out var sortKey)
            || TryParseArticleFromBodyLead(bodyText, out sortKey))
        {
            return ((int)(sortKey / 100), (int)(sortKey % 100), sortKey);
        }

        return (0, 0, 0);
    }

    private static bool TryParseArticleFromText(string? text, out long sortKey)
    {
        sortKey = 0;
        return !string.IsNullOrWhiteSpace(text)
            && RagArticleQueryParser.TryParseArticleSortKey(text, out sortKey);
    }

    private static bool TryParseArticleFromBodyLead(string? bodyText, out long sortKey)
    {
        sortKey = 0;
        if (string.IsNullOrWhiteSpace(bodyText))
            return false;

        foreach (var rawLine in bodyText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            return RagArticleQueryParser.TryParseArticleSortKey(line, out sortKey);
        }

        return false;
    }

    public static string ExtractPenaltyLead(string headerText, string body, string parentText)
    {
        var scope = !string.IsNullOrWhiteSpace(parentText)
            ? parentText
            : string.IsNullOrWhiteSpace(headerText)
                ? body
                : headerText + "\n" + body;
        return RagPenaltyTextHelper.ExtractLeadingPenaltySentence(scope) ?? "";
    }

    public static string ResolveChunkKind(string headerText, string parentText)
    {
        if (string.IsNullOrWhiteSpace(headerText) && string.IsNullOrWhiteSpace(parentText))
            return "fallback";
        return !string.IsNullOrWhiteSpace(parentText) ? "split" : "section";
    }
}
