namespace LocalCompanion.Services;

/// <summary>フェンス無しでもコピーボタンを付けたい塊（SD / Midjourney プロンプト、シェルコマンド等）。表は対象外。</summary>
public static class ChatCopyableBlobDetector
{
    private static readonly string[] CommandPrefixes =
    [
        "dotnet ", "git ", "powershell ", "pwsh ", "cmd ", "pip ", "pip3 ",
        "npm ", "npx ", "python ", "py ", "cargo ", "curl ", "adb ",
        "ffmpeg ", "winget ", "choco ", "uv ",
    ];

    public static bool IsCopyableBlob(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = UnwrapQuotes(text.Trim());
        if (t.Length < 8)
            return false;

        if (ChatMarkdownLayoutNormalizer.LooksLikeMarkdownTable(t))
            return false;

        if (HasCopyableLabel(t) || HasImagineFlags(t) || LooksLikeCommand(t))
            return true;

        return LooksLikeTagList(t);
    }

    public static string TextForDisplay(string text) => UnwrapQuotes(text.Trim());

    private static bool LooksLikeCommand(string text)
    {
        var t = text.Trim();
        if (t.StartsWith(".\\", StringComparison.Ordinal) || t.StartsWith("./", StringComparison.Ordinal))
            return t.Length >= 8;

        if (t.StartsWith("& '", StringComparison.Ordinal) || t.StartsWith("& \"", StringComparison.Ordinal))
            return true;

        foreach (var prefix in CommandPrefixes)
        {
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = t[prefix.Length..];
            if (rest.Length == 0)
                return false;

            var c = rest[0];
            return c <= 0x7F && (char.IsLetterOrDigit(c) || c is '-' or '.' or '/' or '\\' or '"' or '\'');
        }

        return false;
    }

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
