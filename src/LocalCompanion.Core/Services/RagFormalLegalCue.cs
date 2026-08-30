namespace LocalCompanion.Services;

/// <summary>厳密な法令引用（VERBATIM）向けのフォーマルな質問かどうか。</summary>
internal static class RagFormalLegalCue
{
    public static bool IsFormalLegalQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        if (RagArticleQueryParser.TryGetArticleNumber(query, out _)
            && RagLegalQueryContext.LooksLikeLegalArticleQuery(query, sourceHint: null))
            return true;

        return query.Contains("全文", StringComparison.Ordinal)
            || query.Contains("原文", StringComparison.Ordinal)
            || query.Contains("そのまま", StringComparison.Ordinal)
            || query.Contains("引用", StringComparison.Ordinal)
            || query.Contains("正確に", StringComparison.Ordinal)
            || query.Contains("厳密", StringComparison.Ordinal)
            || query.Contains("罰則は", StringComparison.Ordinal)
            || query.Contains("何条", StringComparison.Ordinal)
            || query.Contains("第", StringComparison.Ordinal) && query.Contains("条", StringComparison.Ordinal)
            || query.Contains("RAGを参照", StringComparison.OrdinalIgnoreCase);
    }
}
