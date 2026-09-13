namespace LocalCompanion;

public sealed class LlamaOptions
{
    public const string SectionName = "LlamaCompanion";

    public string DataDirectory { get; set; } = "";
    public string LlamaServerBaseUrl { get; set; } = "http://127.0.0.1:8080";
    public string LlamaModel { get; set; } = "";
    /// <summary>GGUF 置き場（空なら プロジェクト直下 models）</summary>
    public string ModelsDirectory { get; set; } = "";
    /// <summary>キャラ設定 JSON 置き場（空なら プロジェクト直下 characters）</summary>
    public string CharactersDirectory { get; set; } = "";
    public string ModelGgufPath { get; set; } = "";
    public string MmprojGgufPath { get; set; } = "";
    public string EmbedModel { get; set; } = "";
    public int GpuLayers { get; set; } = 99;
    /// <summary>機械に合わせて llama-server の追加引数を付ける。オフなら従来どおり。</summary>
    public bool AutoLaunchTune { get; set; } = true;
    public int ContextLength { get; set; } = 16384;
    public int MaxOutputTokens { get; set; } = 4096;
    /// <summary>Gemma 4 公式推奨は 1.0。</summary>
    public double Temperature { get; set; } = 1.0;
    public double TopP { get; set; } = 0.95;
    /// <summary>llama-server へ渡す top_k（0 以下なら送信しない）。Gemma 4 推奨は 64。</summary>
    public int TopK { get; set; } = 64;
    public int ChunkSize { get; set; } = 900;
    public int ChunkOverlap { get; set; } = 128;
    public int RagTopK { get; set; } = 5;
    /// <summary>長期記憶をプロンプトへ注入する最大件数。</summary>
    public int MemoryTopK { get; set; } = 5;
    /// <summary>ハイブリッド検索で各レーン（FTS / ベクトル）から集める候補数。</summary>
    public int RagSearchPoolSize { get; set; } = 50;
    /// <summary>Reciprocal Rank Fusion の k（通常 60）。</summary>
    public int RagRrfK { get; set; } = 60;
    /// <summary>RRF における FTS 重み（条文・キーワード向け）。</summary>
    public double RagWeightFts { get; set; } = 0.4;
    /// <summary>RRF におけるベクトル重み（言い換え質問向け）。</summary>
    public double RagWeightVec { get; set; } = 0.6;
    /// <summary>チャット添付テキストの最大文字数（超えた分は省略）</summary>
    public int MaxAttachTextChars { get; set; } = 8000;
    /// <summary>この文字数以下のテキスト添付では RAG 検索も併用する。</summary>
    public int RagLightAttachMaxChars { get; set; } = 3000;
    /// <summary>参考資料 1 件の本文上限（字）。親セクション全文でコンテキストを食い尽くさない。</summary>
    public int RagPromptHitMaxChars { get; set; } = Services.RagPromptBudget.DefaultHitChars;
    /// <summary>参考資料ブロック全体の上限（字）。</summary>
    public int RagPromptBlockMaxChars { get; set; } = Services.RagPromptBudget.DefaultBlockChars;
    /// <summary>概念グラフ: 共起辺を残す最小の合計重み（refers 辺は対象外）。</summary>
    public double RagGraphMinEdgeWeight { get; set; } = Services.RagCooccurrenceGraphBuilder.DefaultMinEdgeWeight;
    /// <summary>概念グラフ: 1 チャンクからペア化に使う上位語数。</summary>
    public int RagGraphTopTerms { get; set; } = Services.RagConceptExtractor.DefaultTopK;
    /// <summary>一般質問の RRF に足す「同コミュニティ」レーンの重み（0 で無効）。</summary>
    public double RagWeightGraph { get; set; } = 0.2;
    /// <summary>俯瞰検索: 関連判定に使う LLM 呼び出しの上限回数（0 で退化版のみ）。</summary>
    public int RagGraphRelevanceBudget { get; set; } = 12;
    /// <summary>俯瞰検索: 全体の時間予算（ms）。切れたらそこまでの結果で返す。</summary>
    public int RagGraphTimeBudgetMs { get; set; } = 8000;
    /// <summary>メッセージ内 URL を自動取得する最大件数。</summary>
    public int InlineUrlMaxCount { get; set; } = 3;
    /// <summary>インライン URL 1件あたりの最大文字数。</summary>
    public int InlineUrlMaxChars { get; set; } = 12000;
    /// <summary>調査意図時に Web 検索してコンテキストを足す。</summary>
    public bool WebSearchEnabled { get; set; } = true;
    /// <summary>検索エンドポイント（空なら DuckDuckGo HTML）。SearXNG 等を指定可。</summary>
    public string WebSearchBaseUrl { get; set; } = "";
    /// <summary>Web 検索で返す件数。</summary>
    public int WebSearchTopK { get; set; } = 3;
    /// <summary>検索ヒットのうち本文取得する件数。</summary>
    public int WebSearchFetchTopK { get; set; } = 2;
    /// <summary>AI返答の最大文字数（表示・読み上げの上限）</summary>
    public int MaxReplyChars { get; set; } = 10_000;
    /// <summary>長文返答用の出力トークン上限（MaxReplyChars に合わせて調整）</summary>
    public int MaxReplyOutputTokens { get; set; } = 6144;
    /// <summary>AI 構造化取込の1ウィンドウあたり最大文字数。</summary>
    public int RagStructurerWindowChars { get; set; } = 3500;
    /// <summary>RAG 取込の1ファイル最大バイト数（0 以下で無制限。大きいファイルは取込時のメモリ使用量が増えます）。</summary>
    public long RagMaxFileBytes { get; set; } = 32L * 1024 * 1024;
    /// <summary>フォルダー一括取込の最大ファイル数（0 以下で無制限）。</summary>
    public int RagMaxFolderFiles { get; set; } = 500;
}
