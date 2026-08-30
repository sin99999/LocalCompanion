using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 段落内のインライン Markdown（太字・コード・リンク）を安全な HTML にする。
/// 先に HTML エスケープし、こちらが付けたタグだけを残す。
/// </summary>
public static partial class ChatInlineMarkupHtml
{
    public static string Format(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return FormatOutsideCode(text);
    }

    private static string FormatOutsideCode(string text)
    {
        var sb = new StringBuilder();
        var index = 0;
        foreach (Match match in InlineCodeRegex().Matches(text))
        {
            if (match.Index > index)
                sb.Append(FormatLinksAndBold(text[index..match.Index]));

            sb.Append("<code>").Append(Esc(match.Groups[1].Value)).Append("</code>");
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            sb.Append(FormatLinksAndBold(text[index..]));

        return sb.Length > 0 ? sb.ToString() : ChatConversationHtmlBuilder.LinkifyEscaped(text);
    }

    private static string FormatLinksAndBold(string text)
    {
        var sb = new StringBuilder();
        var index = 0;
        foreach (Match match in MarkdownLinkRegex().Matches(text))
        {
            if (match.Index > index)
                sb.Append(FormatBoldAndUrls(text[index..match.Index]));

            var label = match.Groups[1].Value;
            var rawUrl = match.Groups[2].Value.Trim();
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                sb.Append("<a href=\"")
                    .Append(Esc(uri.AbsoluteUri))
                    .Append("\">")
                    .Append(FormatBoldAndUrls(label))
                    .Append("</a>");
            }
            else
            {
                sb.Append(FormatBoldAndUrls(match.Value));
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
            sb.Append(FormatBoldAndUrls(text[index..]));

        return sb.ToString();
    }

    private static string FormatBoldAndUrls(string text)
    {
        var sb = new StringBuilder();
        var index = 0;
        foreach (Match match in BoldRegex().Matches(text))
        {
            if (match.Index > index)
                sb.Append(ChatConversationHtmlBuilder.LinkifyEscaped(text[index..match.Index]));

            sb.Append("<strong>")
                .Append(ChatConversationHtmlBuilder.LinkifyEscaped(match.Groups[1].Value))
                .Append("</strong>");
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            sb.Append(ChatConversationHtmlBuilder.LinkifyEscaped(text[index..]));

        return sb.ToString();
    }

    private static string Esc(string text) => WebUtility.HtmlEncode(text);

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\*\*([^*\n]+)\*\*")]
    private static partial Regex BoldRegex();
}
