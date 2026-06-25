using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 改行なしで返る Markdown 風テキストを表示用に整形する。
/// DB・読み上げ原文には適用しない。
/// </summary>
public static partial class ChatMarkdownLayoutNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // テーブル行の結合: | ... | | ... |
        t = MergedTableRowRegex().Replace(t, "|\n|");

        // ---### / ---####
        t = HorizontalRuleWithHeadingRegex().Replace(t, "\n---\n${heading}");

        // 単独の ---
        t = HorizontalRuleRegex().Replace(t, "\n---\n");

        // 見出し #〜######
        t = MarkdownHeadingRegex().Replace(t, "\n${heading}");

        // *箇条書き（**太字**は除外）— マッチは * のみ
        t = InlineBulletRegex().Replace(t, "\n* ");

        // 行頭以外の | で始まる表行
        t = InlineTableRowRegex().Replace(t, "\n$0");

        // 行頭|なしの A | B | C 形式を表行に
        t = PromotePipeDelimitedLines(t);

        // 行頭の - リスト（テーブル行内の --- は触らない）
        t = ApplyDashBulletBreaks(t);

        t = ExcessiveNewlinesRegex().Replace(t, "\n\n");
        return t.Trim();
    }

    internal static string StripHeadingPrefix(string line)
    {
        var trimmed = line.Trim();
        return HeadingPrefixRegex().IsMatch(trimmed)
            ? HeadingPrefixRegex().Replace(trimmed, string.Empty).Trim()
            : trimmed;
    }

    internal static bool IsHorizontalRuleLine(string line) =>
        HorizontalRuleOnlyRegex().IsMatch(line.Trim());

    internal static bool IsTableNoiseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return true;

        return TableNoiseLineRegex().IsMatch(trimmed);
    }

    private static string ApplyDashBulletBreaks(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length + 8);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                sb.Append('\n');

            var line = lines[i];
            if (line.Contains('|') || ListLinePrefixRegex().IsMatch(line))
                sb.Append(line);
            else
                sb.Append(InlineDashBulletRegex().Replace(line, "\n- "));
        }

        return sb.ToString();
    }

    private static string PromotePipeDelimitedLines(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('|') || !trimmed.Contains('|'))
                continue;

            if (ListLinePrefixRegex().IsMatch(trimmed))
                continue;

            var cells = trimmed
                .Trim('|')
                .Split('|')
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();

            if (cells.Count >= 2)
                lines[i] = "| " + string.Join(" | ", cells) + " |";
        }

        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"\|\s*\|")]
    private static partial Regex MergedTableRowRegex();

    [GeneratedRegex(@"(?<!\n)---+(?<heading>#{1,6})")]
    private static partial Regex HorizontalRuleWithHeadingRegex();

    [GeneratedRegex(@"(?<!\n)---+(?!#)")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"(?<!\n)(?<heading>#{1,6})(?=\s+\S)")]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(?=[^\s*\n])")]
    private static partial Regex InlineBulletRegex();

    [GeneratedRegex(@"(?<!\n)\|\s*(?=[^\n|]+\|)")]
    private static partial Regex InlineTableRowRegex();

    [GeneratedRegex(@"(?<!\n)-\s+(?=\S)(?<![0-9])")]
    private static partial Regex InlineDashBulletRegex();

    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"^-{3,}$")]
    private static partial Regex HorizontalRuleOnlyRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"^\s*[-*•]\s+|\s*\d+\.\s+")]
    private static partial Regex ListLinePrefixRegex();

    [GeneratedRegex(@"^[-:|]+$|^:?-{1,2}:?$")]
    private static partial Regex TableNoiseLineRegex();
}
