using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>「RAG に何が登録されてる？」など資料一覧の意図を検出する。</summary>
internal static class RagSourceCatalogQueryParser
{
    private static readonly Regex CatalogPattern = new(
        @"(?:RAG|rag|資料|ドキュメント|文書|インデックス|ナレッジ).{0,24}(?:何|一覧|リスト|確認|教えて|ある|入って)|"
        + @"(?:何が|どんな).{0,24}(?:登録|取り込|入って|ある|使える)|"
        + @"(?:登録|取り込).{0,16}(?:資料|ファイル|ソース).{0,16}(?:一覧|リスト|何)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryDetect(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var q = query.Trim();
        if (q.Length < 4)
            return false;

        if (RagArticleQueryParser.TryGetArticleNumber(q, out _))
            return false;

        return CatalogPattern.IsMatch(q);
    }
}
