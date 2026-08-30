using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>「刑法4条と民法104条」のように、法令名と条番号を出現順で組む。</summary>
internal static class RagArticleBindingParser
{
    public static IReadOnlyList<RagArticleBinding> Parse(string query)
    {
        var articles = RagArticleQueryParser.GetArticleNumberSpans(query);
        if (articles.Count == 0)
            return Array.Empty<RagArticleBinding>();

        var hints = RagSourceHintCatalog.FindLegalHintSpans(query);
        if (hints.Count == 0)
            return Array.Empty<RagArticleBinding>();

        var list = new List<RagArticleBinding>();
        string? lastHint = null;
        foreach (var (index, number) in articles)
        {
            string? hint = null;
            for (var i = hints.Count - 1; i >= 0; i--)
            {
                if (hints[i].Index < index)
                {
                    hint = hints[i].Hint;
                    break;
                }
            }

            hint ??= lastHint;
            if (string.IsNullOrWhiteSpace(hint))
                continue;

            lastHint = hint;
            list.Add(new RagArticleBinding(hint, number * 100L));
        }

        return list;
    }
}
