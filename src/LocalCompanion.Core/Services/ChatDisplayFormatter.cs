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
            t = JapaneseSentenceEndRegex().Replace(t, "$1\n");
            t = WesternSentenceEndRegex().Replace(t, "$1\n");

            for (var i = 0; i < protectedSegments.Count; i++)
                t = t.Replace($"{QuotePlaceholderPrefix}{i}\uE001", protectedSegments[i]);
        }

        t = TrailingWhitespaceBeforeNewlineRegex().Replace(t, "\n");
        t = ExcessiveNewlinesRegex().Replace(t, "\n\n");
        return t.Trim();
    }

    /// <summary>句点改行の対象外: 「…」、（…）内。</summary>
    [GeneratedRegex(@"(「[^」]*」|（[^）]*）)")]
    private static partial Regex ProtectedSegmentRegex();

    [GeneratedRegex(@"`[^`\n]+`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"([。！？]+)(?!\n)")]
    private static partial Regex JapaneseSentenceEndRegex();

    /// <summary>英語等: 小数点・略語（ver.）の直後は改行しない。句読点の後は空白・改行・文末のときだけ改行。</summary>
    [GeneratedRegex(@"(?<![0-9])([.!?]+)(?=\s|$|\n)(?!\n)")]
    private static partial Regex WesternSentenceEndRegex();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingWhitespaceBeforeNewlineRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();
}
