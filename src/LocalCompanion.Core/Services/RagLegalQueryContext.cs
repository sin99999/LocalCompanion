using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>条文番号クエリが法令文脈かどうかを判定する（一般資料への誤ルーティング防止）。</summary>
internal static class RagLegalQueryContext
{
    private static readonly Regex LegalCue = new(
        @"刑法|労基|労働基準|民法|会社法|憲法|法律|法令|条文|罰則|国外犯|憲法第|刑法第|労基法|労働基準法",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsLegalSourceHint(string? hint) =>
        !string.IsNullOrWhiteSpace(hint) && RagSourceHintCatalog.IsLegalHint(hint);

    public static bool LooksLikeNamedNonLegalDoc(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("README", StringComparison.OrdinalIgnoreCase)
            || message.Contains("vector", StringComparison.OrdinalIgnoreCase)
            || message.Contains("条項", StringComparison.Ordinal);
    }

    public static bool LooksLikeLegalArticleQuery(string query, string? sourceHint)
    {
        if (IsLegalSourceHint(sourceHint))
            return true;
        if (string.IsNullOrWhiteSpace(query) || LooksLikeNamedNonLegalDoc(query))
            return false;
        if (LegalCue.IsMatch(query))
            return true;

        return RagArticleQueryParser.TryGetArticleNumber(query, out _);
    }
}
