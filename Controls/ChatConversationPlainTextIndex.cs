using System.Text;
using LocalCompanion.Services;

namespace LocalCompanion.Controls;

/// <summary>
/// 会話 RichTextBlock と平行してプレーンテキスト＋URL 位置を保持する。
/// Hyperlink を使わずリンクを開くためのヒット用（ネイティブ選択と Hyperlink の衝突回避）。
/// </summary>
internal sealed class ChatConversationPlainTextIndex
{
    public static ChatConversationPlainTextIndex Shared { get; } = new();

    private readonly StringBuilder _text = new();
    private readonly List<(int Start, int Length, Uri Uri)> _links = new();

    public void Reset()
    {
        _text.Clear();
        _links.Clear();
    }

    public void AppendParagraphBreak()
    {
        if (_text.Length > 0)
            _text.Append('\n');
    }

    public void AppendText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _text.Append(ChatRichTextDisplayNormalizer.Normalize(text));
    }

    public void AppendLink(string urlText, Uri uri)
    {
        var normalized = ChatRichTextDisplayNormalizer.Normalize(urlText);
        var start = _text.Length;
        _text.Append(normalized);
        _links.Add((start, normalized.Length, uri));
    }

    public Uri? ResolveAtOffset(int offset)
    {
        if (offset < 0)
            return null;

        foreach (var (start, length, uri) in _links)
        {
            if (offset >= start && offset < start + length)
                return uri;
        }

        var resolved = ChatMessageUrlExtractor.ResolveUrlAtIndex(_text.ToString(), offset);
        if (resolved is null)
            return null;
        return Uri.TryCreate(resolved, UriKind.Absolute, out var u) ? u : null;
    }
}
