namespace LocalCompanion;

public sealed class VoicevoxOptions
{
    public const string SectionName = "Voicevox";

    public string BaseUrl { get; set; } = "http://127.0.0.1:50021";
    /// <summary>run.exe などを直接指定（空なら標準インストール先を探索）</summary>
    public string EngineExePath { get; set; } = "";
    public bool AutoStart { get; set; } = true;
    public int StartupWaitSeconds { get; set; } = 45;
    public int ProbeTimeoutSeconds { get; set; } = 3;
    public int SynthesisTimeoutSeconds { get; set; } = 180;
    /// <summary>自動読み上げの全文上限（これを超える分は読まない）</summary>
    public int MaxSpeakChars { get; set; } = 10_001;
    /// <summary>1回の合成に渡す塊の目安文字数</summary>
    public int AutoSpeakMaxChars { get; set; } = 150;
    public int SynthesisMaxRetries { get; set; } = 3;
    public bool UpdateCheckOnStartup { get; set; } = true;
    public int UpdateWaitMinutes { get; set; } = 20;

    /// <summary>クレジット表記用の公式サイト URL。</summary>
    public const string OfficialWebsiteUrl = "https://voicevox.hiroshiba.jp/";
}
