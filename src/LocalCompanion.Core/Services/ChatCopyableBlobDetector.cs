namespace LocalCompanion.Services;

/// <summary>フェンス無しでもコピーボタンを付けたい塊（SD / Midjourney プロンプト、シェルコマンド等）。表は対象外。</summary>
public static class ChatCopyableBlobDetector
{
    public readonly record struct CopyablePart(bool IsBlob, string Text);

    private const int HiraganaProseRun = 4;

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

    public static string TextForDisplay(string text) => CleanBlob(text);

    /// <summary>1行を、会話の日本語とコピー用タグ列に分ける。</summary>
    public static IReadOnlyList<CopyablePart> Segment(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Array.Empty<CopyablePart>();

        var t = line.Trim();
        if (ChatMarkdownLayoutNormalizer.LooksLikeMarkdownTable(t))
            return [new CopyablePart(false, t)];

        if (HasCopyableLabel(t) || HasImagineFlags(t) || LooksLikeCommand(t))
            return [new CopyablePart(true, CleanBlob(t))];

        var parts = new List<CopyablePart>();
        foreach (var chunk in SplitJapaneseProse(t))
        {
            var trimmed = chunk.Trim();
            if (trimmed.Length == 0)
                continue;

            parts.Add(ClassifyChunk(trimmed));
        }

        CoalesceAdjacentProse(parts);
        return parts.Count > 0 ? parts : [ClassifyChunk(t)];
    }

    private static void CoalesceAdjacentProse(List<CopyablePart> parts)
    {
        if (parts.Count < 2)
            return;

        var merged = new List<CopyablePart>(parts.Count);
        foreach (var part in parts)
        {
            if (!part.IsBlob && merged.Count > 0 && !merged[^1].IsBlob)
                merged[^1] = new CopyablePart(false, merged[^1].Text + part.Text);
            else
                merged.Add(part);
        }

        parts.Clear();
        parts.AddRange(merged);
    }

    private static CopyablePart ClassifyChunk(string text)
    {
        if (IsCopyableBlob(text)
            || LooksLikeTagList(TrimLeadingCommas(UnwrapQuotes(text.Trim())))
            || LooksLikePeeledTagList(text))
            return new CopyablePart(true, CleanBlob(text));

        return new CopyablePart(false, text.Trim());
    }

    private static bool LooksLikePeeledTagList(string text)
    {
        var t = TrimLeadingCommas(UnwrapQuotes(text.Trim()));
        if (t.Length < 40)
            return false;
        if (FindHiraganaRun(t, 0, HiraganaProseRun) >= 0)
            return false;
        if (t.Contains(". ", StringComparison.Ordinal) || t.Contains('。'))
            return false;

        var commas = 0;
        foreach (var ch in t)
        {
            if (ch == ',')
                commas++;
        }

        return commas >= 4;
    }

    private static string CleanBlob(string text)
    {
        var t = UnwrapQuotes(text.Trim());
        t = StripCommaAfterPromptLabel(t);
        if (!LooksLikeCommand(t) && !HasImagineFlags(t))
            t = TrimLeadingCommas(t);

        return t.Trim();
    }

    private static string StripCommaAfterPromptLabel(string text)
    {
        if (!HasCopyableLabel(text))
            return text;

        var colon = text.IndexOf(':');
        var wide = text.IndexOf('：');
        var idx = colon < 0 ? wide
            : wide < 0 ? colon
            : Math.Min(colon, wide);
        if (idx < 0 || idx + 1 >= text.Length)
            return text;

        var rest = TrimLeadingCommas(text[(idx + 1)..].TrimStart());
        return string.Concat(text.AsSpan(0, idx + 1), " ", rest);
    }

    private static string TrimLeadingCommas(string text)
    {
        var span = text.AsSpan().TrimStart();
        while (span.Length > 0 && span[0] == ',')
            span = span[1..].TrimStart();

        return span.ToString();
    }

    private static List<string> SplitJapaneseProse(string text)
    {
        var chunks = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var run = FindHiraganaRun(text, i, HiraganaProseRun);
            if (run < 0)
            {
                chunks.Add(text[i..]);
                break;
            }

            var (start, end) = ExpandProseSpan(text, run);
            if (start > i)
                chunks.Add(text[i..start]);
            if (end > start)
                chunks.Add(text[start..end]);
            i = end;
        }

        return chunks;
    }

    private static int FindHiraganaRun(string text, int from, int minLength)
    {
        var run = 0;
        var start = -1;
        for (var i = from; i < text.Length; i++)
        {
            if (IsHiragana(text[i]))
            {
                if (run == 0)
                    start = i;
                run++;
                if (run >= minLength)
                    return start;
            }
            else
            {
                run = 0;
                start = -1;
            }
        }

        return -1;
    }

    private static (int Start, int End) ExpandProseSpan(string text, int hiraganaStart)
    {
        var start = hiraganaStart;
        while (start > 0)
        {
            var c = text[start - 1];
            if (c == ',')
                break;
            if (IsAsciiTagChar(c))
                break;
            start--;
        }

        while (start < hiraganaStart && char.IsWhiteSpace(text[start]))
            start++;

        var end = hiraganaStart;
        while (end < text.Length)
        {
            var c = text[end];
            if (c == ',')
                break;
            if (IsAsciiTagChar(c))
                break;
            end++;
        }

        return (start, end);
    }

    // 半角 ()[]{} は SD の強調にも日本語の補足にも使う。タグ区切りはカンマ。
    private static bool IsAsciiTagChar(char c) =>
        c <= 0x7F
        && !char.IsWhiteSpace(c)
        && c is not ',' and not '!' and not '?' and not '.'
            and not '(' and not ')' and not '[' and not ']' and not '{' and not '}';

    private static bool IsHiragana(char c) => c is >= '\u3041' and <= '\u3096';

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

        if (FindHiraganaRun(text, 0, HiraganaProseRun) >= 0)
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
