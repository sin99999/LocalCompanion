using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 「資料全体で何が書いてある？」「主なテーマは？」「A と B の違いを比較して」のような、
/// 1 チャンクでは答えられない俯瞰質問を見つける。条番号があるときは条文経路に譲る。
/// 比較（<see cref="TryDetectComparison"/>）は罰則・定義の機械引用より先に見る。
/// 全体・概要（<see cref="TryDetect"/>）は罰則・定義・FAQ の後、資料一覧の前に見る（「罰則の概要」は罰則のまま）。
/// </summary>
internal static class RagOverviewQueryParser
{
    private static readonly Regex ComparisonCue = new(
        @"比較|違い|使い分け|\bcompare\b|\bdifference\b|\bversus\b|\bvs\.?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OverviewCue = new(
        @"全体|主なテーマ|テーマ|概要|要約|何が書いて|なにが書いて|何について|どんな内容|どういう内容|傾向|ざっくり|俯瞰|"
        + @"\boverview\b|\bsummar(?:y|ize|ise)\b|\bmain (?:topics|themes|points)\b|\bwhat is .* about\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>2 つ以上の主題を並べて比べる質問（条番号なし）。</summary>
    public static bool TryDetectComparison(string? query) =>
        HasCue(query, ComparisonCue);

    /// <summary>資料全体・概要・主題の質問（条番号なし）。</summary>
    public static bool TryDetect(string? query) =>
        HasCue(query, OverviewCue) || HasCue(query, ComparisonCue);

    private static bool HasCue(string? query, Regex cue)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        if (RagArticleQueryParser.TryGetArticleNumber(query, out _))
            return false;

        return cue.IsMatch(query);
    }
}
