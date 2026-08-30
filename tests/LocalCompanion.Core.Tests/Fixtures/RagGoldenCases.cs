namespace LocalCompanion.Core.Tests.Fixtures;

internal enum RagGoldenKind
{
    /// <summary>雑談 → RAG Skip。</summary>
    Skip,

    /// <summary>モードだけ確認（検索はしない）。</summary>
    Mode,

    /// <summary>構造化検索で条文ヒット。</summary>
    ArticleHit,

    /// <summary>構造化検索でミス。</summary>
    ArticleMiss,

    /// <summary>境界条。</summary>
    BoundaryHit,

    /// <summary>定義ヒット（entry_key）。</summary>
    DefinitionHit,

    /// <summary>合成ヒットに対する SoftTopic フィルタ／順位。</summary>
    SoftPolicy,

    /// <summary>RiskCaution が法令ソースを優先。</summary>
    RiskPolicy,

    /// <summary>verbatim 成功／失敗の文言。</summary>
    Verbatim,

    /// <summary>検索失敗と miss の指示文。</summary>
    PromptCopy,
}

internal sealed record RagGoldenCase(
    string Id,
    RagGoldenKind Kind,
    string Query,
    string? ExpectMode = null,
    string? ExpectSourceContains = null,
    long? ExpectArticleSortKey = null,
    string? ExpectHeaderContains = null,
    string? ExpectReplyContains = null,
    bool ExpectMiss = false,
    bool ExpectSoftEmpty = false,
    bool ExpectSoftKeepsLegal = false,
    bool ExpectRiskKeepsLegal = false,
    bool ExpectRankFirstSourceContains = false,
    bool SearchFailedPrompt = false);

internal static class RagGoldenCases
{
    /// <summary>約30問。ライブ精度ではなく回帰の黄金セット。</summary>
    public static IReadOnlyList<RagGoldenCase> All { get; } =
    [
        // --- Skip / Mode (1-8) ---
        new("skip-chitchat", RagGoldenKind.Skip, "AIさんは女の子ですか？可愛らしく感じます。"),
        new("skip-greeting", RagGoldenKind.Skip, "おはよう、今日もよろしくね"),
        new("mode-soft-overtime", RagGoldenKind.Mode, "残業の法律ってどうなってる？", ExpectMode: "SoftTopic"),
        new("mode-soft-rag-word", RagGoldenKind.Mode, "RAGの資料を参照して説明して", ExpectMode: "SoftTopic"),
        new("mode-risk-steal", RagGoldenKind.Mode, "万引きしたら捕まる？", ExpectMode: "RiskCaution"),
        new("mode-risk-fraud", RagGoldenKind.Mode, "闇バイトって危ないの？", ExpectMode: "RiskCaution"),
        new("mode-structured-article", RagGoldenKind.Mode, "刑法第8条の全文を教えて", ExpectMode: "Structured"),
        new("mode-structured-labor", RagGoldenKind.Mode, "労働基準法第11条を条文どおり出して", ExpectMode: "Structured"),

        // --- ArticleHit (9-16) ---
        new("hit-penal-8", RagGoldenKind.ArticleHit, "刑法第8条の全文",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 800, ExpectHeaderContains: "第8条"),
        new("hit-penal-7", RagGoldenKind.ArticleHit, "刑法第7条の本文",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 700, ExpectHeaderContains: "第7条"),
        new("hit-penal-1", RagGoldenKind.ArticleHit, "刑法第1条",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 100),
        new("hit-penal-20", RagGoldenKind.ArticleHit, "刑法第20条の全文を出して",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 2000),
        new("hit-labor-11", RagGoldenKind.ArticleHit, "労働基準法第11条の全文",
            ExpectSourceContains: "労働基準法", ExpectArticleSortKey: 1100, ExpectHeaderContains: "第11条"),
        new("hit-labor-32", RagGoldenKind.ArticleHit, "労働基準法第32条",
            ExpectSourceContains: "労働基準法", ExpectArticleSortKey: 3200, ExpectHeaderContains: "第32条"),
        new("hit-penal-8-keibo-hint", RagGoldenKind.ArticleHit, "刑法の第8条を見せて",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 800),
        new("hit-labor-11-body", RagGoldenKind.ArticleHit, "労働基準法第11条の本文を、条文どおり全文だけ出して。",
            ExpectSourceContains: "労働基準法", ExpectArticleSortKey: 1100),

        // --- ArticleMiss / Boundary / Definition (17-22) ---
        new("miss-penal-999", RagGoldenKind.ArticleMiss, "刑法第999条の全文",
            ExpectArticleSortKey: 99900, ExpectMiss: true),
        new("miss-labor-999", RagGoldenKind.ArticleMiss, "労働基準法第999条",
            ExpectArticleSortKey: 99900, ExpectMiss: true),
        new("boundary-first-penal", RagGoldenKind.BoundaryHit, "刑法の最初の条文は？",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 100),
        new("boundary-last-penal", RagGoldenKind.BoundaryHit, "刑法の最後の条文は？",
            ExpectSourceContains: "刑法", ExpectArticleSortKey: 2000),
        new("def-komuin", RagGoldenKind.DefinitionHit, "公務員とは",
            ExpectSourceContains: "刑法", ExpectReplyContains: "公務"),
        new("def-chingin-via-article", RagGoldenKind.ArticleHit, "労働基準法第11条",
            ExpectSourceContains: "労働基準法", ExpectHeaderContains: "賃金"),

        // --- Soft / Risk policy (23-27) ---
        new("soft-no-article-merge", RagGoldenKind.Mode, "残業の法律ってどうなってる？",
            ExpectMode: "SoftTopic"),
        new("soft-drop-unrelated", RagGoldenKind.SoftPolicy, "残業の法律ってどうなってる？",
            ExpectSoftEmpty: true),
        new("soft-keep-overtime-token", RagGoldenKind.SoftPolicy, "残業の法律ってどうなってる？",
            ExpectSoftKeepsLegal: true),
        new("soft-rank-overtime-first", RagGoldenKind.SoftPolicy, "残業 四十時間 法律",
            ExpectRankFirstSourceContains: true),
        new("risk-prefer-penal", RagGoldenKind.RiskPolicy, "万引きしたら捕まる？",
            ExpectRiskKeepsLegal: true),
        new("risk-mode-only", RagGoldenKind.Mode, "盗んだら捕まるの？", ExpectMode: "RiskCaution"),

        // --- Verbatim / Prompt (28-30) ---
        new("verbatim-miss-999", RagGoldenKind.Verbatim, "労働基準法第999条の全文を出して。無かったら、無い、ってだけ言って。",
            ExpectMiss: true, ExpectReplyContains: "見つかりませんでした"),
        new("verbatim-hit-11", RagGoldenKind.Verbatim, "労働基準法第11条の本文を、条文どおり全文だけ出して。",
            ExpectReplyContains: "賃金"),
        new("prompt-search-failed", RagGoldenKind.PromptCopy, "",
            SearchFailedPrompt: true, ExpectReplyContains: "検索未完了"),
    ];
}
