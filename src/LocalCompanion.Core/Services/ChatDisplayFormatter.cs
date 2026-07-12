using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// チャット表示用テキスト整形。DB・読み上げ用の原文は変更しない。
/// </summary>
public static partial class ChatDisplayFormatter
{
    private const char QuotePlaceholderPrefix = '\uE000';

    public static string FormatForDisplay(string? text, bool sentenceBreaks = true)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');

        if (sentenceBreaks)
        {
            var protectedSegments = new List<string>();
            t = ProtectedSegmentRegex().Replace(t, match =>
            {
                protectedSegments.Add(match.Value);
                return $"{QuotePlaceholderPrefix}{protectedSegments.Count - 1}\uE001";
            });

            t = InlineCodeRegex().Replace(t, match =>
            {
                protectedSegments.Add(match.Value);
                return $"{QuotePlaceholderPrefix}{protectedSegments.Count - 1}\uE001";
            });

            t = FencedCodeRegex().Replace(t, match =>
            {
                protectedSegments.Add(match.Value);
                return $"{QuotePlaceholderPrefix}{protectedSegments.Count - 1}\uE001";
            });

            // モデルが改行しないときの表示用（保存・読み上げ原文は変えない）
            // ！！！… は句点群の末尾で1回だけ改行（各 ! ごとに改行しない）
            // 直後が絵文字等の記号なら改行しない（！😘💕 を同列に保つ）
            // ※ 絵文字はサロゲートペアなので \p{So} だけでは拾えない → Rune 判定
            var source = t;
            t = JapaneseSentenceEndRegex().Replace(source, match =>
            {
                var next = match.Index + match.Length;
                if (IsEmojiOrSymbolDecoration(source, next))
                    return match.Groups[1].Value;
                return match.Groups[1].Value + "\n";
            });
            t = WesternSentenceEndRegex().Replace(t, "$1\n");

            // ？💕A. のように絵文字の直後に選択肢ラベルが付くときはラベルを次行へ
            t = InsertNewlineBeforeLetteredOptions(t);

            for (var i = 0; i < protectedSegments.Count; i++)
                t = t.Replace($"{QuotePlaceholderPrefix}{i}\uE001", protectedSegments[i]);
        }

        t = TrailingWhitespaceBeforeNewlineRegex().Replace(t, "\n");
        t = ExcessiveNewlinesRegex().Replace(t, "\n\n");
        return t.Trim();
    }

    /// <summary>句点直後が絵文字・装飾記号なら表示用改行を入れない。</summary>
    private static bool IsEmojiOrSymbolDecoration(string text, int index)
    {
        if ((uint)index >= (uint)text.Length)
            return false;

        if (!Rune.TryGetRuneAt(text, index, out var rune))
            return false;

        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol;
    }

    /// <summary>絵文字・装飾記号の直後の A. B. などの前に改行を入れる。</summary>
    private static string InsertNewlineBeforeLetteredOptions(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var matches = LetteredOptionMarkerRegex().Matches(text);
        if (matches.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length + matches.Count);
        var last = 0;
        foreach (Match match in matches)
        {
            var start = match.Index;
            if (start > last)
                sb.Append(text, last, start - last);

            if (start > 0 && sb.Length > 0 && sb[^1] != '\n'
                && IsEmojiOrSymbolDecoration(text, IndexBeforeLetteredOption(text, start)))
                sb.Append('\n');

            sb.Append(match.Value);
            last = start + match.Length;
        }

        if (last < text.Length)
            sb.Append(text, last, text.Length - last);

        return sb.ToString();
    }

    private static int IndexBeforeLetteredOption(string text, int letterIndex)
    {
        if (letterIndex <= 0)
            return 0;

        var index = letterIndex - 1;
        if (char.IsLowSurrogate(text[index]) && index > 0)
            index--;

        return index;
    }

    /// <summary>句点改行の対象外: 「…」、（…）内、半角 (…) 内。</summary>
    [GeneratedRegex(@"(「[^」]*」|（[^）]*）|\([^)\n]*\))")]
    private static partial Regex ProtectedSegmentRegex();

    [GeneratedRegex(@"`[^`\n]+`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"([。！？]+)(?!\n)")]
    private static partial Regex JapaneseSentenceEndRegex();

    /// <summary>英語等: 小数点・略語（ver.）・選択肢 A. の直後は改行しない。</summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z])([.!?]+)(?=\s|$|\n)(?!\n)")]
    private static partial Regex WesternSentenceEndRegex();

    [GeneratedRegex(@"[A-Z]\.\s")]
    private static partial Regex LetteredOptionMarkerRegex();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingWhitespaceBeforeNewlineRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();
}
