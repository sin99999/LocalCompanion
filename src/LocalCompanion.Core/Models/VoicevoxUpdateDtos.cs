namespace LocalCompanion.Models;

public sealed record VoicevoxUpdateCheckResult(
    bool Installed,
    string? CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? InstallerUrl,
    string? InstallerFileName);

public sealed record VoicevoxSpeakerCacheEntry(int Id, string SpeakerName, string StyleName);
