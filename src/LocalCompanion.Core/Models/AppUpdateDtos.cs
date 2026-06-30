namespace LocalCompanion.Models;

public sealed record AppUpdateCheckResult(
    string? CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleasePageUrl);
