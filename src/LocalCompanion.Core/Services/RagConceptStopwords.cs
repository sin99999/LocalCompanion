using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 概念にしない機能語（法令・一般・英語）。正規化後（小文字・NFKC）の語で比較する。
/// Windows の WordsSegmenter は「及び」「処する」「受け」「対し」のような接続語・活用形も語として返すので、
/// 明示の表に加えて活用形の形（漢字 1 字＋ひらがな尾、漢字＋する、第N項）でも落とす。
/// </summary>
internal static class RagConceptStopwords
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // 法令の骨組み
        "第", "条", "項", "号", "前項", "前条", "次条", "同条", "同項", "本条", "本法", "本編", "本章", "各号", "各項",
        "当該", "規定", "場合", "以下", "以上", "以内", "未満", "超", "その他", "他", "又", "及", "若", "並",
        "及び", "又は", "若しくは", "並びに", "かつ", "ただし", "但し", "なお", "また", "この", "その", "同様", "準用",
        "者", "こと", "もの", "とき", "ため", "ところ", "限", "限り", "例示", "本文", "全文", "掲げる", "該当",
        // 一般語
        "今日", "明日", "昨日", "時間", "場所", "方法", "説明", "質問", "回答", "資料", "情報", "内容",
        "関係", "関連", "確認", "必要", "可能", "利用", "使用", "実行", "設定", "表示", "選択", "参照",
        // 英語の機能語・汎用語
        "the", "and", "for", "with", "this", "that", "from", "into", "are", "was", "were", "not", "you",
        "can", "will", "have", "has", "had", "but", "all", "any", "may", "use", "used", "using",
        "md", "txt", "pdf", "http", "https", "www", "com",
    };

    // 漢字 1 字＋ひらがな 1〜3 字（受け・対し・犯し・定め・限り・係る・掲げ・用い）
    private static readonly Regex OneKanjiInflection = new(@"^\p{IsCJKUnifiedIdeographs}\p{IsHiragana}{1,3}$", RegexOptions.Compiled);

    // 漢字 1〜3 字＋する系（処する・罰する・関する・適用する・定める）
    private static readonly Regex SuruVerb = new(@"^\p{IsCJKUnifiedIdeographs}{1,3}(?:する|される|できる|せる|める|げる|ける|れる)$", RegexOptions.Compiled);

    // 第N項・第一項（項の参照は概念にしない。条は別で拾う）
    private static readonly Regex ParagraphRef = new(@"^第?[0-9一二三四五六七八九十百]+項$", RegexOptions.Compiled);

    public static bool IsStopword(string normalizedTerm)
    {
        if (string.IsNullOrWhiteSpace(normalizedTerm) || Words.Contains(normalizedTerm))
            return true;

        return OneKanjiInflection.IsMatch(normalizedTerm)
            || SuruVerb.IsMatch(normalizedTerm)
            || ParagraphRef.IsMatch(normalizedTerm);
    }
}
