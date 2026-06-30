using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

/// <summary>GitHub Releases から LocalCompanion 本体の新バージョンを確認する。</summary>
public sealed class AppUpdateService
{
    private static readonly Regex RepoPattern = new(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppUpdateOptions _opt;
    private readonly AppUpdateDismissStore _dismiss;
    private readonly ILogger<AppUpdateService> _log;
    private readonly HttpClient _github;

    public AppUpdateService(
        IOptions<AppUpdateOptions> opt,
        AppUpdateDismissStore dismiss,
        ILogger<AppUpdateService> log)
    {
        _opt = opt.Value;
        _dismiss = dismiss;
        _log = log;
        _github = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _github.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanionWinUI/1.0");
        _github.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var current = AppVersionInfo.Current();
        if (!_opt.UpdateCheckOnStartup || !IsValidRepo(_opt.GitHubRepo))
            return new AppUpdateCheckResult(current, null, false, null);

        if (!_dismiss.ShouldCheckNow(_opt.CheckIntervalHours))
            return new AppUpdateCheckResult(current, null, false, null);

        _dismiss.MarkChecked();

        var latest = await FetchLatestReleaseAsync(_opt.GitHubRepo, ct);
        if (latest is null)
            return new AppUpdateCheckResult(current, null, false, null);

        var updateAvailable = VoicevoxUpdateService.IsNewerVersion(latest.Value.Version, current);
        return new AppUpdateCheckResult(
            current,
            latest.Value.Version,
            updateAvailable,
            latest.Value.PageUrl);
    }

    public bool ShouldPrompt(AppUpdateCheckResult check) =>
        check.UpdateAvailable
        && !string.IsNullOrWhiteSpace(check.LatestVersion)
        && _dismiss.ShouldOffer(check.LatestVersion!);

    public void Dismiss(AppUpdateCheckResult check)
    {
        if (!string.IsNullOrWhiteSpace(check.LatestVersion))
            _dismiss.DismissVersion(check.LatestVersion);
    }

    internal static string? ParseReleaseTag(string? tagName) =>
        VoicevoxUpdateService.NormalizeVersion(tagName);

    private static bool IsValidRepo(string repo) =>
        !string.IsNullOrWhiteSpace(repo) && RepoPattern.IsMatch(repo.Trim());

    private async Task<(string Version, string PageUrl)?> FetchLatestReleaseAsync(string repo, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo.Trim()}/releases/latest";
        try
        {
            using var res = await _github.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogDebug("App update check HTTP {Status} for {Repo}", (int)res.StatusCode, repo);
                return null;
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var version = ParseReleaseTag(tag);
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var pageUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(pageUrl))
                pageUrl = $"https://github.com/{repo.Trim()}/releases/latest";

            return (version, pageUrl);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "App update check failed for {Repo}", repo);
            return null;
        }
    }
}
