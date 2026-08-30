using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>VERBATIM 意図で資料特定に失敗したとき LLM 創作へ流さない。</summary>
internal static class RagVerbatimGuard
{
    public static bool ShouldBlockLlm(RagQueryPlan plan) =>
        plan.ResponseMode == RagResponseMode.Verbatim
        && plan.Intent is RagQueryIntent.Article
            or RagQueryIntent.Penalty
            or RagQueryIntent.Boundary
            or RagQueryIntent.Definition
            or RagQueryIntent.Faq
            or RagQueryIntent.SourceCatalog;

    public static string BuildMissReply(RagQueryPlan plan, bool japanese)
    {
        if (plan.Intent == RagQueryIntent.SourceCatalog)
        {
            return japanese
                ? "RAG に登録された資料はまだありません。Settings → RAG からファイルを取り込んでください。"
                : "No sources are registered for RAG yet. Import files from Settings → RAG.";
        }

        if (plan.ArticleSortKey is > 0)
        {
            var label = RagArticleQueryParser.FormatArticleLabel(plan.ArticleSortKey.Value);
            return japanese
                ? $"登録資料を探しましたが、{label}は見つかりませんでした。"
                : $"Searched the registered materials, but {label} was not found.";
        }

        return japanese
            ? "登録資料を探しましたが、該当箇所は見つかりませんでした。"
            : "Searched the registered materials, but no matching passage was found.";
    }
}
