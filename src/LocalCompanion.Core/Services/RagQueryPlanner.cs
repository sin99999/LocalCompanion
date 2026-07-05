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

        if (RagArticleQueryParser.TryGetArticleNumber(effective, out var articleNumber)
            && RagLegalQueryContext.LooksLikeLegalArticleQuery(effective, sourceHint))
        {
            var wantsVerbatim = ContainsVerbatimCue(effective);
            var wantsPenalty = effective.Contains('罰', StringComparison.Ordinal);
            return new RagQueryPlan(
                RagQueryIntent.Article,
                effective,
                ArticleSortKey: articleNumber * 100L,
                Boundary: null,
                TopicKeyword: null,
                SourceHint: sourceHint,
                SourceHints: sourceHints,
                ResponseMode: wantsVerbatim || wantsPenalty ? RagResponseMode.Verbatim : RagResponseMode.CitationFirst,
                Confidence: 0.88);
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

    private static bool ContainsVerbatimCue(string query) =>
        query.Contains("全文", StringComparison.Ordinal)
        || query.Contains("原文", StringComparison.Ordinal)
        || query.Contains("そのまま", StringComparison.Ordinal)
        || query.Contains("引用", StringComparison.Ordinal);
}
