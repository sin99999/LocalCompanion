namespace LocalCompanion.Services;

/// <summary>フェンス無しでもコピーボタンを付けたい塊（SD / Midjourney プロンプト等）。</summary>
public static class ChatCopyableBlobDetector
{
    public static bool IsCopyableBlob(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = UnwrapQuotes(text.Trim());
        if (t.Length < 8)
            return false;

        if (HasCopyableLabel(t) || HasImagineFlags(t))
            return true;

        return LooksLikeTagList(t);
    }

    public static string TextForDisplay(string text) => UnwrapQuotes(text.Trim());

    private static bool HasCopyableLabel(string text)
    {
        var start = text.TrimStart('(').TrimStart();
        return start.StartsWith("Negative Prompt", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("Positive Prompt", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("Prompt:", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("ネガティブプロンプト", StringComparison.Ordinal)
            || start.StartsWith("ポジティブプロンプト", StringComparison.Ordinal)
            || start.StartsWith("/imagine", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasImagineFlags(string text) =>
        text.Contains("--ar ", StringComparison.Ordinal)
        || text.Contains("--v ", StringComparison.Ordinal)
        || text.Contains("--stylize", StringComparison.OrdinalIgnoreCase)
        || text.Contains("--niji", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTagList(string text)
    {
        if (text.Length < 80)
            return false;

        var commas = 0;
        var sentenceEnds = 0;
        foreach (var ch in text)
        {
            if (ch == ',')
                commas++;
            else if (ch is '。' or '！' or '？')
                sentenceEnds++;
        }

        return commas >= 8 && sentenceEnds <= 1;
    }

    private static string UnwrapQuotes(string text)
    {
        if (text.Length < 2)
            return text;

        if (text[0] == '「' && text[^1] == '」')
            return text[1..^1].Trim();
        if (text[0] == '『' && text[^1] == '』')
            return text[1..^1].Trim();
        return text;
    }
}
