namespace LocalCompanion.Models;

public sealed class AppSettingsDto
{
    public const double DefaultChatFontSize = 14;

    public const string DefaultChatFontFamily = "Segoe UI Variable Text";

    public bool ConfirmHistoryDelete { get; set; } = true;

    public string ThemeMode { get; set; } = AppThemeModes.Dark;

    public string ChatFontFamily { get; set; } = DefaultChatFontFamily;

    public double ChatFontSize { get; set; } = DefaultChatFontSize;

    /// <summary>チャット表示名および AI プロンプト用。空のときはローカライズされた「あなた」。</summary>
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>HTML 取込時に見出し付き Markdown 風テキストへ変換する。</summary>
    public bool RagUseHtmlMarkdown { get; set; } = true;

    /// <summary>取込時にローカル LLM で資料を構造化する（時間がかかります）。</summary>
    public bool RagUseLlmStructurer { get; set; }

    /// <summary>AI 構造化結果をユーザーデータ配下 rag-cache に保存する。</summary>
    public bool RagSaveStructurerCache { get; set; } = true;

    /// <summary>PDF 取込時にレイアウト解析（見出し・ヘッダ除去）を使う。</summary>
    public bool RagUsePdfLayoutReader { get; set; }

    /// <summary>会話をまたいで覚える長期記憶を有効にする。</summary>
    public bool MemoryEnabled { get; set; } = true;

    /// <summary>会話終了時に LLM で記憶を自動抽出する。</summary>
    public bool MemoryAutoExtractOnClose { get; set; } = true;

    /// <summary>名前付きキャラの会話で、履歴が溢れた分を短い要約として残す。</summary>
    public bool HistoryDigestEnabled { get; set; } = true;

    /// <summary>
    /// 名前付きキャラが性格・指示（persona）の変更を提案することを許可する。
    /// 提案は毎回ユーザー確認後のみ JSON に保存する（デフォルト OFF）。
    /// </summary>
    public bool CharacterSelfImproveEnabled { get; set; }

    /// <summary>過去メッセージの意味検索（UI は撤去済み。設定互換のため保持）。</summary>
    public bool ChatSearchEnabled { get; set; }

    /// <summary>音声入力（Windows 音声認識）を有効にする。</summary>
    public bool SpeechInputEnabled { get; set; }

    /// <summary>起動のたびに llama.cpp の latest を見て古ければ取り直す。オフでも初回導入と GPU 切替は行う。</summary>
    public bool LlamaCppAutoUpdate { get; set; } = true;

    /// <summary>資料を探すかどうかの判断に Julia-1 を使う。会話の文章は書かない。</summary>
    public bool JuliaDecisionEnabled { get; set; } = true;

    /// <summary>auto / gpu / cpu。auto は重みが空き VRAM に全部入るときだけ GPU。</summary>
    public string JuliaDevice { get; set; } = "auto";

    /// <summary>起動のたびに Julia-1 の配布元を見て、変わっていれば取り直す。オフでも未導入時は 1 回取得する。</summary>
    public bool JuliaAutoUpdate { get; set; } = true;

    /// <summary>NVIDIA 以外: auto / hip-radeon / vulkan。NVIDIA は CUDA 固定。</summary>
    public string GpuBackendPreference { get; set; } = LlamaGpuBackendPreferences.Auto;

    /// <summary>チャット状態欄に直前の生成 tok/s を出す。</summary>
    public bool ShowChatTokenSpeed { get; set; }

    /// <summary>以前の手動スイッチ。起動時は見ない。ファイル名に MTP があるときだけ投機デコードを使う。</summary>
    public bool SpecDraftMtp { get; set; }

    public static AppSettingsDto CreateDefault() => new();

    public AppSettingsDto Clone() => new()
    {
        ConfirmHistoryDelete = ConfirmHistoryDelete,
        ThemeMode = ThemeMode,
        ChatFontFamily = ChatFontFamily,
        ChatFontSize = ChatFontSize,
        UserDisplayName = UserDisplayName,
        RagUseHtmlMarkdown = RagUseHtmlMarkdown,
        RagUseLlmStructurer = RagUseLlmStructurer,
        RagSaveStructurerCache = RagSaveStructurerCache,
        RagUsePdfLayoutReader = RagUsePdfLayoutReader,
        MemoryEnabled = MemoryEnabled,
        MemoryAutoExtractOnClose = MemoryAutoExtractOnClose,
        HistoryDigestEnabled = HistoryDigestEnabled,
        CharacterSelfImproveEnabled = CharacterSelfImproveEnabled,
        ChatSearchEnabled = ChatSearchEnabled,
        SpeechInputEnabled = SpeechInputEnabled,
        LlamaCppAutoUpdate = LlamaCppAutoUpdate,
        JuliaDecisionEnabled = JuliaDecisionEnabled,
        JuliaDevice = JuliaDevice,
        JuliaAutoUpdate = JuliaAutoUpdate,
        GpuBackendPreference = GpuBackendPreference,
        ShowChatTokenSpeed = ShowChatTokenSpeed,
        SpecDraftMtp = SpecDraftMtp,
    };
}

public static class AppThemeModes
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";
}
