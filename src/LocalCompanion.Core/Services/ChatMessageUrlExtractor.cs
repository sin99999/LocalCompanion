using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>チャット文面中の URL 1件（表示位置付き）。</summary>
internal readonly record struct ChatUrlSpan(string Url, int Start, int Length);

/// <summary>リンク化用のテキスト断片。</summary>
internal readonly record struct ChatTextSegment(bool IsUrl, string Text);

/// <summary>チャット文面から http(s) URL を抽出する。</summary>
internal static class ChatMessageUrlExtractor
{
    // 末尾の日本語句読点・閉じ括弧は URL に含めない
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""'）】」』>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly char[] TrailingJunk = ['.', ',', ';', ':', '!', '?', '。', '、', '）', ')', '】', '」', '』', ']'];

    public static IReadOnlyList<string> Extract(string? message, int maxCount = 5)
    {
        var spans = ExtractSpans(message, maxCount);
        if (spans.Count == 0)
            return Array.Empty<string>();
        return spans.Select(s => s.Url).ToArray();
    }

    public static IReadOnlyList<ChatUrlSpan> ExtractSpans(string? message, int maxCount = 64)
    {
        if (string.IsNullOrWhiteSpace(message) || maxCount <= 0)
            return Array.Empty<ChatUrlSpan>();

        var found = new List<ChatUrlSpan>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UrlRegex.Matches(message))
        {
            var raw = TrimTrailingJunk(match.Value);
            if (raw.Length < 8)
                continue;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("http" or "https"))
                continue;
            if (!seen.Add(uri.AbsoluteUri))
                continue;

            found.Add(new ChatUrlSpan(uri.AbsoluteUri, match.Index, raw.Length));
            if (found.Count >= maxCount)
                break;
        }

        return found;
    }

    /// <summary>キャレット位置が URL 上（または直後）ならその URL を返す。</summary>
    public static string? ResolveUrlAtIndex(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret < 0)
            return null;

        foreach (var span in ExtractSpans(text, maxCount: 64))
        {
            var end = span.Start + span.Length;
            if (caret >= span.Start && caret <= end)
                return span.Url;
        }

        return null;
    }

    /// <summary>URL を区切りとしてテキストを分割する（表示用。重複 URL も位置どおり残す）。</summary>
    public static IReadOnlyList<ChatTextSegment> SplitByUrls(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSegment>();

        var segments = new List<ChatTextSegment>();
        var index = 0;
        foreach (Match match in UrlRegex.Matches(text))
        {
            var raw = TrimTrailingJunk(match.Value);
            if (raw.Length < 8
                || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            if (match.Index > index)
                segments.Add(new ChatTextSegment(false, text[index..match.Index]));

            segments.Add(new ChatTextSegment(true, uri.AbsoluteUri));
            index = match.Index + raw.Length;
        }

        if (index < text.Length)
            segments.Add(new ChatTextSegment(false, text[index..]));

        return segments.Count == 0
            ? [new ChatTextSegment(false, text)]
            : segments;
    }

    private static string TrimTrailingJunk(string value)
    {
        var end = value.Length;
        while (end > 0 && TrailingJunk.Contains(value[end - 1]))
            end--;
        return end == value.Length ? value : value[..end];
    }
}
