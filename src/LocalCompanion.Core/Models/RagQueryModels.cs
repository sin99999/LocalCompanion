namespace LocalCompanion.Models;

public enum RagArticleBoundaryIntent
{
    First,
    Last,
}

public enum RagQueryIntent
{
    General,
    Article,
    Boundary,
    Penalty,
    Definition,
    Advisory,
}

public enum RagResponseMode
{
    Synthesis,
    CitationFirst,
    Verbatim,
    PersonaSynthesis,
}

public sealed record RagQueryPlan(
    RagQueryIntent Intent,
    string EffectiveQuery,
    long? ArticleSortKey,
    RagArticleBoundaryIntent? Boundary,
    string? TopicKeyword,
    string? SourceHint,
    IReadOnlyList<string>? SourceHints,
    RagResponseMode ResponseMode,
    double Confidence);

public sealed record RagSearchResult(
    IReadOnlyList<RagSearchHit> Hits,
    RagQueryPlan Plan);
