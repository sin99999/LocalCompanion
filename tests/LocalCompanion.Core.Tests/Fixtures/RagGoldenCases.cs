namespace LocalCompanion.Core.Tests.Fixtures;

internal enum RagGoldenKind
{
    /// <summary>雑談 → 何も載せない（門番で Skip、または Probe の語彙検索に根拠が無い）。</summary>
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

    /// <summary>合図語が無くても、資料に根拠があれば RAG を載せる（根拠門番）。</summary>
    EvidenceAttach,

    /// <summary>資料全体・比較の質問は Overview 意図になる。</summary>
    Overview,

    /// <summary>条文の参照関係（第N条から第M条まで）を辿って別条に届く。</summary>
    MultiHop,
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
        // 前後差分（根拠門番）: 旧 "skip-chitchat" は合図語なし＝Skip だった。今は雑談メモに同じ語（AI・女の子）が実在するので載る。
        new("evidence-chitchat-in-notes", RagGoldenKind.EvidenceAttach, "AIさんは女の子ですか？可愛らしく感じます。",
            ExpectSourceContains: "雑談メモ"),
        new("skip-greeting", RagGoldenKind.Skip, "おはよう、今日もよろしくね"),
        new("skip-tired", RagGoldenKind.Skip, "今日は疲れたなあ"),
        new("skip-thanks", RagGoldenKind.Skip, "ありがとう、助かった"),

        // --- 根拠門番: 合図語なしの資料質問で RAG が動く ---
        new("evidence-readme-build", RagGoldenKind.EvidenceAttach, "ビルドはどうやるの？",
            ExpectSourceContains: "README", ExpectHeaderContains: "ビルド"),
        new("evidence-readme-webview2", RagGoldenKind.EvidenceAttach, "WebView2 って入れないとだめ？",
            ExpectSourceContains: "README"),
        new("evidence-readme-data-dir", RagGoldenKind.EvidenceAttach, "データってどこに保存されるの",
            ExpectSourceContains: "README", ExpectHeaderContains: "保存場所"),

        // --- Overview 意図（資料全体・比較。条番号があれば条文経路のまま） ---
        new("overview-all-themes", RagGoldenKind.Overview, "登録してある資料全体で、主なテーマを教えて"),
        new("overview-what-inside", RagGoldenKind.Overview, "この資料には何が書いてあるの？概要をまとめて"),
        new("overview-compare", RagGoldenKind.Overview, "窃盗と強盗の違いを比較して", ExpectSourceContains: "窃盗"),
        new("overview-not-article-compare", RagGoldenKind.Mode, "刑法4条と104条の違いを比較して", ExpectMode: "Structured"),

        // --- 多段: 第243条は「窃盗」の語を含まないが、第235条への参照（グラフ）で届く ---
        new("multihop-theft-attempt", RagGoldenKind.MultiHop, "窃盗の未遂って罰せられる？",
            ExpectSourceContains: "窃盗", ExpectHeaderContains: "第243条"),
        new("multihop-theft-relative", RagGoldenKind.MultiHop, "親族の間で窃盗したらどうなる？",
            ExpectSourceContains: "窃盗", ExpectHeaderContains: "第244条"),
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

    /// <summary>
    /// RAG 作り直し（根拠門番・Overview・グラフ）の評価セット。
    /// 実装が入った段で <see cref="All"/> へ移す。ここにある間は ID の重複だけを検査する。
    /// </summary>
    public static IReadOnlyList<RagGoldenCase> Planned { get; } = [];
}
