namespace LocalCompanion.Services;

/// <summary>取り込み時に条文番号・罰則文言などを構造化する。</summary>
internal static class LegalFieldExtractor
{
    public static (int Main, int Sub, long SortKey) ParseArticle(string headerText)
    {
        if (!RagArticleQueryParser.TryParseArticleSortKey(headerText, out var sortKey))
            return (0, 0, 0);

        return ((int)(sortKey / 100), (int)(sortKey % 100), sortKey);
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
