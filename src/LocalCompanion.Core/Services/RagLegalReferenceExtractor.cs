using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 条文本文の参照（「第235条」「前条」「前二条」「第235条から第236条まで」「…の規定を準用」）を決定論的に解き、
/// 条ノード間の <c>refers</c> 辺にする。LLM なし。多段質問（「窃盗罪で準用される規定は？」）の 1〜2 hop 用。
/// </summary>
internal static class RagLegalReferenceExtractor
{
    /// <summary>「第N条から第M条まで」を展開する最大幅（誤検出で数百ノードに繋がないため）。</summary>
    public const int MaxRangeSpan = 20;

    private static readonly Regex RangePattern = new(
        @"第\s*([0-9０-９]{1,3})\s*条\s*から\s*第\s*([0-9０-９]{1,3})\s*条\s*まで",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RelativePattern = new(
        @"(前|次)(二|三|2|3)?条",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>本文が参照している条番号（自分自身は含まない。昇順・重複なし）。</summary>
    public static IReadOnlyList<int> ExtractReferencedArticles(int articleMain, string? text)
    {
        var set = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();

        foreach (Match m in RangePattern.Matches(text))
        {
            if (!TryParse(m.Groups[1].Value, out var from) || !TryParse(m.Groups[2].Value, out var to))
                continue;
            if (to < from || to - from > MaxRangeSpan)
                continue;
            for (var n = from; n <= to; n++)
                set.Add(n);
        }

        foreach (var (_, number) in RagArticleQueryParser.GetArticleNumberSpans(text))
            set.Add(number);

        if (articleMain > 0)
        {
            foreach (Match m in RelativePattern.Matches(text))
            {
                var count = m.Groups[2].Success ? (m.Groups[2].Value is "二" or "2" ? 2 : 3) : 1;
                var direction = m.Groups[1].Value == "前" ? -1 : 1;
                for (var i = 1; i <= count; i++)
                {
                    var n = articleMain + direction * i;
                    if (n > 0)
                        set.Add(n);
                }
            }
        }

        set.Remove(articleMain);
        return set.ToList();
    }

    /// <summary>条ノード間の参照辺（正規化形「第N条」どうし）。</summary>
    public static IReadOnlyList<RagConceptEdgeDraft> BuildEdges(int articleMain, string? text)
    {
        if (articleMain <= 0)
            return Array.Empty<RagConceptEdgeDraft>();

        var self = RagConceptExtractor.Normalize($"第{articleMain}条");
        return ExtractReferencedArticles(articleMain, text)
            .Select(n => new RagConceptEdgeDraft(self, RagConceptExtractor.Normalize($"第{n}条"), RagConceptEdgeDraft.KindRefers, 1.0))
            .ToList();
    }

    private static bool TryParse(string digits, out int value)
    {
        var normalized = new string(digits.Select(ch => ch is >= '０' and <= '９' ? (char)('0' + (ch - '０')) : ch).ToArray());
        return int.TryParse(normalized, out value) && value > 0;
    }
}
