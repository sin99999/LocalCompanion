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

        if (!NeedsTopicFromHistory(current))
            return current;

        return $"{previousUserMessage.Trim()} {current}";
    }

    private static bool NeedsTopicFromHistory(string message)
    {
        if (FollowUpPattern.IsMatch(message))
            return true;

        return message.Length < 28
            && !message.Contains('条', StringComparison.Ordinal)
            && !RagArticleQueryParser.TryGetArticleNumber(message, out _);
    }
}
