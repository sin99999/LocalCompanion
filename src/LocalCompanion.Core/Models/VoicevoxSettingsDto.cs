namespace LocalCompanion.Models;

public sealed class VoicevoxSettingsDto
{
    public bool Enabled { get; set; }
    public bool AutoSpeak { get; set; } = true;
    /// <summary>画面表示が外国語のとき、読み上げ用に日本語へ翻訳して VOICEVOX で再生します。</summary>
    public bool SpeakInJapanesePronunciation { get; set; }
    public int SpeakerId { get; set; }
    public bool SpeakerChosenByUser { get; set; }
    public double SpeedScale { get; set; } = 1.0;
    public double PitchScale { get; set; } = 0.0;
    public double IntonationScale { get; set; } = 1.0;
    public double VolumeScale { get; set; } = 1.0;
    public double PrePhonemeLength { get; set; } = 0.1;
    public double PostPhonemeLength { get; set; } = 0.1;
}

public sealed record VoicevoxSpeakerStyleDto(int Id, string SpeakerName, string StyleName);

public sealed record VoicevoxInstallDto(bool Installed, string? LauncherPath, string? Hint);

public sealed record VoicevoxStatusDto(
    bool Available,
    bool Installed,
    bool ManagedByApp,
    string BaseUrl,
    string? Version,
    string? Hint);
