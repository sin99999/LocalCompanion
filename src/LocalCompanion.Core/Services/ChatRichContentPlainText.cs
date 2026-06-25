using LocalCompanion.Models;
using System.Text;

namespace LocalCompanion.Services;

/// <summary>表示・コピー用の連続プレーンテキスト（段落・箇条書きを1本の文字列にまとめる）。</summary>
public static class ChatRichContentPlainText
{
    public static string Build(string? sourceText, bool sentenceBreaks = true, string? headerPrefix = null)
    {
        var blocks = ChatRichContentParser.ParseBlocks(sourceText, sentenceBreaks);
        if (blocks.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(headerPrefix))
                return sourceText ?? string.Empty;

            return headerPrefix;
        }

        return FormatBlocks(blocks, headerPrefix);
    }

    public static string BuildConversation(IReadOnlyList<ChatMessageDisplayPart> messages) =>
        BuildDisplayText(messages).Text;

    public static string FormatMessage(ChatMessageDisplayPart part) =>
        BuildDisplayText(new[] { part }).Text;

    public static ChatConversationDisplayText BuildDisplayText(IReadOnlyList<ChatMessageDisplayPart> messages)
    {
        if (messages.Count == 0)
            return new ChatConversationDisplayText();

        var sb = new StringBuilder();
        var reasoningRanges = new List<ChatTextRange>();

        for (var i = 0; i < messages.Count; i++)
        {
            var part = messages[i];
            if (string.IsNullOrWhiteSpace(part.Text) && string.IsNullOrWhiteSpace(part.ReasoningText))
                continue;

            if (sb.Length > 0)
                sb.AppendLine().AppendLine();

            if (!string.IsNullOrWhiteSpace(part.Header))
            {
                sb.Append(part.Header);
                if (!string.IsNullOrWhiteSpace(part.ReasoningText) || !string.IsNullOrWhiteSpace(part.Text))
                    sb.AppendLine().AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(part.ReasoningText))
            {
                var reasoningText = FormatBlocks(
                    ChatRichContentParser.ParseBlocks(part.ReasoningText, sentenceBreaks: true));
                if (reasoningText.Length > 0)
                {
                    var start = sb.Length;
                    sb.Append(reasoningText);
                    reasoningRanges.Add(new ChatTextRange(start, reasoningText.Length));
                }

                if (!string.IsNullOrWhiteSpace(part.Text))
                    sb.AppendLine().AppendLine();
            }

            sb.Append(Build(part.Text, part.ApplySentenceBreaks, headerPrefix: null));
        }

        return new ChatConversationDisplayText
        {
            Text = sb.ToString(),
            ReasoningRanges = reasoningRanges,
        };
    }

    public static string FormatBlocks(IReadOnlyList<ChatDisplayBlock> blocks, string? headerPrefix = null)
    {
        if (blocks.Count == 0)
            return headerPrefix ?? string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(headerPrefix))
            sb.Append(headerPrefix);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0 || !string.IsNullOrWhiteSpace(headerPrefix))
                sb.AppendLine().AppendLine();

            AppendBlock(sb, blocks[i]);
        }

        return sb.ToString();
    }

    internal static void AppendBlock(StringBuilder sb, ChatDisplayBlock block)
    {
        switch (block.Kind)
        {
            case ChatDisplayBlockKind.Paragraph:
                sb.Append(string.Join('\n', block.ParagraphLines.Select(ChatTableBoxFormatter.SanitizeCell)));
                break;
            case ChatDisplayBlockKind.List:
                AppendList(sb, block.ListOrdered, block.ListItems);
                break;
            case ChatDisplayBlockKind.Table:
                AppendTable(sb, block.TableHeader, block.TableRows);
                break;
            case ChatDisplayBlockKind.Code:
                sb.Append(block.CodeText);
                break;
        }
    }

    public static string FormatBlock(ChatDisplayBlock block)
    {
        var sb = new StringBuilder();
        AppendBlock(sb, block);
        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, bool ordered, IReadOnlyList<string> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();

            var marker = ordered ? $"{i + 1}. " : "• ";
            sb.Append(marker).Append(ChatTableBoxFormatter.SanitizeCell(items[i]));
        }
    }

    private static void AppendTable(
        StringBuilder sb,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (header.Count > 0)
        {
            sb.Append("| ")
                .Append(string.Join(" | ", header.Select(ChatTableBoxFormatter.SanitizeCell)))
                .AppendLine(" |");
        }

        foreach (var row in rows)
        {
            sb.Append("| ")
                .Append(string.Join(" | ", row.Select(ChatTableBoxFormatter.SanitizeCell)))
                .AppendLine(" |");
        }
    }
}
