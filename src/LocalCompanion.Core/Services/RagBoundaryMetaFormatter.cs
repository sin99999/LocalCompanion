using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>境界条（最初／最後）の機械引用文。画面言語に合わせる。</summary>
internal static class RagBoundaryMetaFormatter
{
    public static string Format(
        RagArticleBoundaryIntent boundary,
        string sourceName,
        string label,
        AppLanguage? language = null)
    {
        var lang = language
            ?? LocalizationService.Instance?.Current
            ?? AppLanguage.Japanese;
        var key = boundary == RagArticleBoundaryIntent.Last
            ? "Chat.Rag.Boundary.Last"
            : "Chat.Rag.Boundary.First";
        var table = LocalizationResources.For(lang);
        if (!table.TryGetValue(key, out var template))
            template = LocalizationResources.For(AppLanguage.Japanese)[key];
        return string.Format(template, sourceName, label);
    }
}
