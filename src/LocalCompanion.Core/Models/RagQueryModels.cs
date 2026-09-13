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
    Faq,
    Advisory,
    SourceCatalog,

    /// <summary>資料全体の主題・要約・比較（条番号なし）。概念グラフのコミュニティ単位で探す。</summary>
    Overview,
}

public enum RagResponseMode
{
    Synthesis,
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
    double Confidence,
    IReadOnlyList<long>? ArticleSortKeys = null,
    IReadOnlyList<RagArticleBinding>? ArticleBindings = null);

public sealed record RagArticleBinding(string Hint, long SortKey);

public sealed record RagSearchResult(
    IReadOnlyList<RagSearchHit> Hits,
    RagQueryPlan Plan,
    /// <summary>タイムアウト・例外などで検索自体が完了しなかったとき true（ヒット無しとは別）。</summary>
    bool SearchFailed = false);
