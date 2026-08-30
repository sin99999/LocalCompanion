using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>RAG 検索意図と応答モードを一括で決める。</summary>
internal static class RagQueryPlanner
{
    public static RagQueryPlan Plan(string currentMessage, string? previousUserMessage)
    {
        var effective = RagSearchQueryComposer.Compose(currentMessage, previousUserMessage);
        var sourceHint = RagSourceHintCatalog.ExtractPrimaryHint(effective);
        var sourceHints = RagSourceHintCatalog.ExtractAllHints(effective);

        if (RagArticleQueryParser.TryGetBoundaryIntent(effective, out var boundary))
        {
            return new RagQueryPlan(
                RagQueryIntent.Boundary,
                effective,
                ArticleSortKey: null,
                Boundary: boundary,
                TopicKeyword: null,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.92);
        }

        var articleNumbers = RagArticleQueryParser.GetArticleNumbers(effective);
        if (articleNumbers.Count > 0
            && RagLegalQueryContext.LooksLikeLegalArticleQuery(effective, sourceHint))
        {
            var bindings = RagArticleBindingParser.Parse(effective);
            var keys = bindings.Count > 0
                ? bindings.Select(static b => b.SortKey).Distinct().ToList()
                : articleNumbers.Select(static n => n * 100L).ToList();
            return new RagQueryPlan(
                RagQueryIntent.Article,
                effective,
                ArticleSortKey: keys[0],
                Boundary: null,
                TopicKeyword: null,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.88,
                ArticleSortKeys: keys,
                ArticleBindings: bindings.Count > 0 ? bindings : null);
        }

        if (RagPenaltyTopicParser.TryGetTopicKeyword(effective, out var topic))
        {
            return new RagQueryPlan(
                RagQueryIntent.Penalty,
                effective,
                ArticleSortKey: null,
                Boundary: null,
                TopicKeyword: topic,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.9);
        }

        if (RagDefinitionQueryParser.TryGetTerm(effective, out var term))
        {
            return new RagQueryPlan(
                RagQueryIntent.Definition,
                effective,
                ArticleSortKey: null,
                Boundary: null,
                TopicKeyword: RagEntryKeyNormalizer.Normalize(term),
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.85);
        }

        if (RagFaqQueryParser.TryGetQuestion(effective, out var faqKey))
        {
            return new RagQueryPlan(
                RagQueryIntent.Faq,
                effective,
                ArticleSortKey: null,
                Boundary: null,
                TopicKeyword: faqKey,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.84);
        }

        if (RagSourceCatalogQueryParser.TryDetect(effective))
        {
            return new RagQueryPlan(
                RagQueryIntent.SourceCatalog,
                effective,
                ArticleSortKey: null,
                Boundary: null,
                TopicKeyword: null,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: RagResponseMode.Verbatim,
                Confidence: 0.95);
        }

        if (RagAdvisoryQueryParser.TryDetect(effective, out var advisoryHints))
        {
            return new RagQueryPlan(
                RagQueryIntent.Advisory,
                effective,
                ArticleSortKey: null,
                Boundary: null,
                TopicKeyword: null,
                SourceHint: advisoryHints.Count > 0 ? advisoryHints[0] : sourceHint,
                SourceHints: advisoryHints,
                ResponseMode: RagResponseMode.PersonaSynthesis,
                Confidence: 0.82);
        }

        return new RagQueryPlan(
            RagQueryIntent.General,
            effective,
            ArticleSortKey: null,
            Boundary: null,
            TopicKeyword: null,
            SourceHint: sourceHint,
            SourceHints: sourceHints,
            ResponseMode: RagResponseMode.Synthesis,
            Confidence: 0.5);
    }
}
