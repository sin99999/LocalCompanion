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
                ? $"登録資料から{label}を特定できませんでした。Settings → RAG で該当資料を再取込するか、条番号付き Markdown（#### 第N条）で取り込んでください。"
                : $"Could not locate {label} in registered materials. Re-import the source from Settings → RAG, preferably as Markdown with #### Article N headings.";
        }

        return japanese
            ? "登録資料から該当箇所を特定できませんでした。資料の再取込または質問の言い換えをお試しください。"
            : "Could not locate a matching passage in registered materials. Try re-importing sources or rephrasing the question.";
    }
}
