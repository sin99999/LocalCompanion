using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>条番号だけで複数法令が当たったときの案内。本文は混ぜない。</summary>
internal static class RagArticleAmbiguousFormatter
{
    public static string Format(
        RagQueryPlan plan,
        IReadOnlyList<string> sourceNames,
        AppLanguage? language = null)
    {
        var lang = language
            ?? LocalizationService.Instance?.Current
            ?? AppLanguage.Japanese;
        var table = LocalizationResources.For(lang);
        if (!table.TryGetValue("Chat.Rag.Article.Ambiguous", out var template))
            template = LocalizationResources.For(AppLanguage.Japanese)["Chat.Rag.Article.Ambiguous"];

        var label = plan.ArticleSortKey is > 0
            ? RagArticleQueryParser.FormatArticleLabel(plan.ArticleSortKey.Value)
            : "";
        var list = string.Join(" / ", sourceNames);
        return string.Format(template, label, list);
    }
}
