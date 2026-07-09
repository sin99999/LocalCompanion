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
