using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.Controls;

/// <summary>
/// 会話／メッセージの RichTextBlock を組み立てる。
/// URL は Hyperlink ではなく下線付き Run（選択中のネイティブ AV / ハイライト消失を避ける）。
/// </summary>
internal static class ChatRichTextDocumentBuilder
{
    private static readonly FontFamily CodeFontFamily = new("Cascadia Mono, Consolas, Courier New");

    private static string FontFamilyName = "Segoe UI";
    private static double FontSize = 14;

    /// <summary>会話全体ビュー構築時だけプレーンテキスト索引を更新する。</summary>
    public static bool TrackPlainTextIndex { get; set; }

    public static event Action? AppearanceChanged;

    public static void SetAppearance(string fontFamily, double fontSize)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        var size = fontSize > 0 ? fontSize : 14;
        if (string.Equals(FontFamilyName, family, StringComparison.Ordinal)
            && Math.Abs(FontSize - size) < 0.01)
        {
            return;
        }

        FontFamilyName = family;
        FontSize = size;
        AppearanceChanged?.Invoke();
    }

    public static void ApplyHostStyle(RichTextBlock host, bool secondaryForeground = false)
    {
        host.FontFamily = new FontFamily(FontFamilyName);
        host.FontSize = FontSize;
        if (secondaryForeground)
        {
            host.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            host.FontSize = Math.Max(12, FontSize - 2);
        }
        else
        {
            host.ClearValue(RichTextBlock.ForegroundProperty);
        }

        // 選択色は WinUI 既定（TextSelectionHighlightColorThemeBrush）
        host.ClearValue(RichTextBlock.SelectionHighlightColorProperty);
    }

    /// <summary>表示 Blocks は触らず、リンク用プレーンテキスト索引だけ更新する。</summary>
    public static void IndexMessageOnly(
        string? header,
        string? reasoningText,
        string? bodyText,
        bool applySentenceBreaks,
        bool addLeadingSpacer)
    {
        if (addLeadingSpacer)
            ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();

        if (!string.IsNullOrWhiteSpace(header))
        {
            ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
            ChatConversationPlainTextIndex.Shared.AppendText(header);
        }

        if (!string.IsNullOrWhiteSpace(reasoningText))
            IndexParsedText(reasoningText, sentenceBreaks: true);

        if (!string.IsNullOrWhiteSpace(bodyText))
            IndexParsedText(bodyText, applySentenceBreaks);
    }

    private static void IndexParsedText(string sourceText, bool sentenceBreaks)
    {
        var parsed = ChatRichContentParser.ParseBlocks(sourceText, sentenceBreaks);
        if (parsed.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
                IndexTextWithLinks(sourceText);
            }
            return;
        }

        foreach (var block in parsed)
        {
            if (block.Kind == ChatDisplayBlockKind.Code)
            {
                ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
                ChatConversationPlainTextIndex.Shared.AppendText(block.CodeText);
                continue;
            }

            var text = ChatRichContentPlainText.FormatBlock(block);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
            IndexTextWithLinks(text);
        }
    }

    private static void IndexTextWithLinks(string text)
    {
        if (text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
        {
            ChatConversationPlainTextIndex.Shared.AppendText(text);
            return;
        }

        foreach (var segment in ChatMessageUrlExtractor.SplitByUrls(text))
        {
            if (segment.IsUrl
                && Uri.TryCreate(segment.Text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                ChatConversationPlainTextIndex.Shared.AppendLink(segment.Text, uri);
            }
            else
            {
                ChatConversationPlainTextIndex.Shared.AppendText(segment.Text);
            }
        }
    }

    /// <summary>1発言分を Blocks 末尾に追加し、追加したブロック数を返す。</summary>
    public static int AppendMessage(
        BlockCollection blocks,
        string? header,
        string? reasoningText,
        string? bodyText,
        bool applySentenceBreaks,
        bool addLeadingSpacer)
    {
        var before = blocks.Count;

        if (addLeadingSpacer && before > 0)
        {
            if (TrackPlainTextIndex)
                ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
            blocks.Add(new Paragraph { Margin = new Thickness(0, 12, 0, 0) });
        }

        if (!string.IsNullOrWhiteSpace(header))
            blocks.Add(CreateParagraph(header, linkify: false, secondary: false, bold: true));

        if (!string.IsNullOrWhiteSpace(reasoningText))
            AppendParsedText(blocks, reasoningText, sentenceBreaks: true, secondary: true);

        if (!string.IsNullOrWhiteSpace(bodyText))
            AppendParsedText(blocks, bodyText, applySentenceBreaks, secondary: false);

        if (blocks.Count == before
            && (!string.IsNullOrWhiteSpace(header)
                || !string.IsNullOrWhiteSpace(reasoningText)
                || !string.IsNullOrWhiteSpace(bodyText)))
        {
            var fallback = BuildFallback(header, reasoningText, bodyText);
            blocks.Add(CreateParagraph(fallback, linkify: true, secondary: false, bold: false));
        }

        return blocks.Count - before;
    }

    public static void AppendParsedText(
        BlockCollection blocks,
        string sourceText,
        bool sentenceBreaks,
        bool secondary)
    {
        var parsed = ChatRichContentParser.ParseBlocks(sourceText, sentenceBreaks);
        if (parsed.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(sourceText))
                blocks.Add(CreateParagraph(sourceText, linkify: true, secondary, bold: false));
            return;
        }

        foreach (var block in parsed)
        {
            if (block.Kind == ChatDisplayBlockKind.Code)
            {
                blocks.Add(CreateCodeParagraph(block.CodeText));
                continue;
            }

            var text = ChatRichContentPlainText.FormatBlock(block);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            blocks.Add(CreateParagraph(text, linkify: true, secondary, bold: false));
        }
    }

    public static Paragraph CreateParagraph(string text, bool linkify, bool secondary, bool bold)
    {
        if (TrackPlainTextIndex)
            ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 4),
        };
        AppendTextInlines(paragraph.Inlines, text, linkify, monospace: false, secondary, bold);
        return paragraph;
    }

    public static Paragraph CreateCodeParagraph(string codeText)
    {
        if (TrackPlainTextIndex)
        {
            ChatConversationPlainTextIndex.Shared.AppendParagraphBreak();
            ChatConversationPlainTextIndex.Shared.AppendText(codeText);
        }
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
        };

        var border = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = codeText,
                FontFamily = CodeFontFamily,
                FontSize = Math.Max(12, FontSize - 1),
                TextWrapping = TextWrapping.WrapWholeWords,
                // 外側 RTB の選択と二重にならないよう内側選択は切る
                IsTextSelectionEnabled = false,
            },
        };

        paragraph.Inlines.Add(new InlineUIContainer { Child = border });
        return paragraph;
    }

    public static void AppendTextInlines(
        InlineCollection inlines,
        string text,
        bool linkify,
        bool monospace,
        bool secondary,
        bool bold)
    {
        var fontFamily = monospace ? CodeFontFamily : new FontFamily(FontFamilyName);
        var fontSize = monospace ? Math.Max(12, FontSize - 1) : FontSize;
        Brush? foreground = null;
        if (secondary)
            foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        if (!linkify || text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
        {
            if (TrackPlainTextIndex)
                ChatConversationPlainTextIndex.Shared.AppendText(text);
            inlines.Add(CreateRun(text, fontFamily, fontSize, foreground, bold));
            return;
        }

        var linkBrush = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        foreach (var segment in ChatMessageUrlExtractor.SplitByUrls(text))
        {
            if (segment.IsUrl
                && Uri.TryCreate(segment.Text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                // Hyperlink は選択ドラッグと衝突してハイライト消失→ネイティブ AV しやすい
                // （microsoft-ui-xaml#9442 / #7299 系）。下線 Run + 自前クリックで開く。
                if (TrackPlainTextIndex)
                    ChatConversationPlainTextIndex.Shared.AppendLink(segment.Text, uri);
                var run = CreateRun(segment.Text, fontFamily, fontSize, linkBrush, bold: false);
                var underline = new Underline();
                underline.Inlines.Add(run);
                inlines.Add(underline);
            }
            else
            {
                if (TrackPlainTextIndex)
                    ChatConversationPlainTextIndex.Shared.AppendText(segment.Text);
                inlines.Add(CreateRun(segment.Text, fontFamily, fontSize, foreground, bold));
            }
        }
    }

    private static Run CreateRun(string text, FontFamily fontFamily, double fontSize, Brush? foreground, bool bold)
    {
        var run = new Run
        {
            // ❤️ 等の FE0F 付き絵文字は選択ハイライトが穴あきになり、ドラッグで WinUI が落ちることがある
            Text = ChatRichTextDisplayNormalizer.Normalize(text),
            FontFamily = fontFamily,
            FontSize = fontSize,
        };
        if (foreground is not null)
            run.Foreground = foreground;
        if (bold)
            run.FontWeight = FontWeights.SemiBold;
        return run;
    }

    private static string BuildFallback(string? header, string? reasoning, string? body)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(header))
            parts.Add(header);
        if (!string.IsNullOrWhiteSpace(reasoning))
            parts.Add(reasoning);
        if (!string.IsNullOrWhiteSpace(body))
            parts.Add(body);
        return string.Join("\n\n", parts);
    }
}
