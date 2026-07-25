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

    /// <summary>
    /// 名前付きキャラが性格・指示（persona）の変更を提案することを許可する。
    /// 提案は毎回ユーザー確認後のみ JSON に保存する（デフォルト OFF）。
    /// </summary>
    public bool CharacterSelfImproveEnabled { get; set; }

    /// <summary>過去メッセージの意味検索（UI は撤去済み。設定互換のため保持）。</summary>
    public bool ChatSearchEnabled { get; set; }

    /// <summary>音声入力（Windows 音声認識）を有効にする。</summary>
    public bool SpeechInputEnabled { get; set; }

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
        CharacterSelfImproveEnabled = CharacterSelfImproveEnabled,
        ChatSearchEnabled = ChatSearchEnabled,
        SpeechInputEnabled = SpeechInputEnabled,
    };
}

public static class AppThemeModes
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";
}
