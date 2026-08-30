namespace LocalCompanion.Services;

/// <summary>追質問の直前が相槌だけのとき、法令トピックを拾う。</summary>
internal static class RagHistoryTopicPicker
{
    public static string? PickPreviousUserTopic(
        IEnumerable<(string Role, string Content)> newestFirst)
    {
        foreach (var (role, content) in newestFirst)
        {
            if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(content))
                continue;
            if (IsTopiclessAck(content))
                continue;
            return content;
        }

        return null;
    }

    internal static bool IsTopiclessAck(string content)
    {
        var t = content.Trim();
        if (t.Length > 18)
            return false;
        if (t.Contains('条', StringComparison.Ordinal)
            || RagLegalQueryContext.LooksLikeLegalArticleQuery(t, sourceHint: null)
            || RagArticleQueryParser.TryGetArticleNumber(t, out _))
            return false;

        return t.Contains("そうなんだ", StringComparison.Ordinal)
            || t.Contains("そうだね", StringComparison.Ordinal)
            || t.Contains("なるほど", StringComparison.Ordinal)
            || t.Contains("ありがとう", StringComparison.Ordinal)
            || t.Equals("うん", StringComparison.Ordinal)
            || t.Equals("わかった", StringComparison.Ordinal)
            || t.Equals("へー", StringComparison.Ordinal)
            || t.Equals("おやすみ", StringComparison.Ordinal);
    }
}
