using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>RAG 検索用クエリを組み立てる（追質問でトピック語が消える問題への対処）。</summary>
internal static class RagSearchQueryComposer
{
    private static readonly Regex FollowUpPattern = new(
        """
        (?:RAG|参考資料|資料(?:DB|ベース)?|登録資料|引用|正確|正しく|もう一度|再度|さっき|先ほど|\
        前(?:の|に)(?:回答|質問|返事)|本当|ちゃんと(?:教|答)|その(?:件|話題|内容)|\
        参照(?:して|し)|見(?:て|直))
        """,
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    public static string Compose(string currentMessage, string? previousUserMessage)
    {
        if (string.IsNullOrWhiteSpace(currentMessage))
            return currentMessage ?? "";

        var current = currentMessage.Trim();
        if (string.IsNullOrWhiteSpace(previousUserMessage))
            return current;

        var previous = previousUserMessage.Trim();
        if (!NeedsTopicFromHistory(current))
            return current;

        // 危険話題は直前の別犯罪（万引き→殺人など）をくっつけない
        if (RagConversationGate.LooksLikeCrimeRisk(current))
            return current;

        // 直前が「第N条」系なのに、今の発話に条が無い → くっつけない
        // （残業の質問が第999条ミスになる汚染を防ぐ）
        if (ShouldBlockArticleHistoryMerge(current, previous))
            return current;

        return $"{previous} {current}";
    }

    private static bool NeedsTopicFromHistory(string message)
    {
        if (FollowUpPattern.IsMatch(message))
            return true;

        if (RagArticleQueryParser.TryGetArticleNumber(message, out _)
            && !RagLegalQueryContext.LooksLikeNamedNonLegalDoc(message)
            && RagSourceHintCatalog.ExtractPrimaryHint(message) is null)
            return true;

        return message.Length < 28
            && !message.Contains('条', StringComparison.Ordinal)
            && !RagArticleQueryParser.TryGetArticleNumber(message, out _);
    }

    /// <summary>直前の条文アンカーを、条なしのソフト／一般質問へ持ち越さない。</summary>
    internal static bool ShouldBlockArticleHistoryMerge(string current, string previous)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(previous))
            return false;

        var previousAnchored = HasArticleAnchor(previous);
        if (!previousAnchored)
            return false;

        // 今の発話自体が条・条番号を持つ追質問（「4条って何？」）はマージしてよい
        if (HasArticleAnchor(current))
            return false;

        return true;
    }

    private static bool HasArticleAnchor(string text) =>
        text.Contains('条', StringComparison.Ordinal)
        || RagArticleQueryParser.TryGetArticleNumber(text, out _);
}
