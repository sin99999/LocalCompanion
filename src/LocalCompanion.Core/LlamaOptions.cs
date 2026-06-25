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
    public int ContextLength { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.8;
    public double TopP { get; set; } = 0.95;
    /// <summary>llama-server へ渡す top_k（0 以下なら送信しない）。Gemma 4 推奨は 64。</summary>
    public int TopK { get; set; } = 64;
    public int ChunkSize { get; set; } = 900;
    public int ChunkOverlap { get; set; } = 128;
    public int RagTopK { get; set; } = 5;
    /// <summary>チャット添付テキストの最大文字数（超えた分は省略）</summary>
    public int MaxAttachTextChars { get; set; } = 8000;
    /// <summary>AI返答の最大文字数（表示・読み上げの上限）</summary>
    public int MaxReplyChars { get; set; } = 10_000;
    /// <summary>長文返答用の出力トークン上限（MaxReplyChars に合わせて調整）</summary>
    public int MaxReplyOutputTokens { get; set; } = 6144;
}
