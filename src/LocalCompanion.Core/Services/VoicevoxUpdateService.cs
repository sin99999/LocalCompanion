using System.Net.Http.Headers;
using System.Text.Json;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

/// <summary>VOICEVOX の GitHub リリースと、インストール済みの本体の版を比較し、更新を適用する。</summary>
public sealed class VoicevoxUpdateService
{
    private const string ReleasesLatestUrl = "https://api.github.com/repos/VOICEVOX/voicevox/releases/latest";

    private readonly VoicevoxClient _client;
    private readonly VoicevoxInstallLocator _locator;
    private readonly VoicevoxLifecycleService _lifecycle;
    private readonly VoicevoxOptions _opt;
    private readonly ILogger<VoicevoxUpdateService> _log;
    private readonly HttpClient _github;

    public VoicevoxUpdateService(
        VoicevoxClient client,
        VoicevoxInstallLocator locator,
        VoicevoxLifecycleService lifecycle,
        IOptions<VoicevoxOptions> opt,
        ILogger<VoicevoxUpdateService> log)
    {
        _client = client;
        _locator = locator;
        _lifecycle = lifecycle;
        _opt = opt.Value;
        _log = log;
        _github = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _github.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanionWinUI/1.0");
        _github.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<VoicevoxUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!_locator.IsInstalled)
            return new VoicevoxUpdateCheckResult(false, null, null, false, null, null);

        var current = await CurrentInstalledVersionAsync(ct);
        var latest = await FetchLatestReleaseAsync(ct);
        if (latest is null)
            return new VoicevoxUpdateCheckResult(true, current, null, false, null, null);

        var release = latest.Value;
        var updateAvailable = !string.IsNullOrWhiteSpace(current)
            && IsNewerVersion(release.ReleaseVersion, current);
        return new VoicevoxUpdateCheckResult(
            true,
            current,
            release.ReleaseVersion,
            updateAvailable,
            release.Url,
            release.FileName);
    }

    public async Task<bool> ApplyUpdateAsync(VoicevoxUpdateCheckResult check, CancellationToken ct = default)
    {
        if (!check.UpdateAvailable || string.IsNullOrWhiteSpace(check.InstallerUrl))
            return false;

        var before = check.CurrentVersion;
        var target = check.LatestVersion;
        var installerPath = await DownloadInstallerAsync(check.InstallerUrl!, check.InstallerFileName ?? "voicevox-setup.exe", ct);
        if (installerPath is null)
            return false;

        _lifecycle.BeginUpdate();
        try
        {
            _lifecycle.StopEngineProcessesForUpdate();
            await Task.Delay(TimeSpan.FromSeconds(2), ct);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "VOICEVOX installer launch failed");
                return false;
            }

            _locator.InvalidateCache();

            var deadline = DateTime.UtcNow.AddMinutes(Math.Max(_opt.UpdateWaitMinutes, 5));
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var now = await CurrentInstalledVersionAsync(ct);
                if (IsReleaseInstalled(target, now))
                {
                    _log.LogInformation("VOICEVOX updated: {Before} -> {After}", before, now);
                    return true;
                }

                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            }

            _log.LogDebug("VOICEVOX update wait timed out (before={Before}, target={Target})", before, target);
            return false;
        }
        finally
        {
            _lifecycle.EndUpdate();
        }
    }

    private async Task<(string ReleaseVersion, string Url, string FileName)?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _github.GetAsync(ReleasesLatestUrl, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var version = NormalizeVersion(tag);
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var launcher = _locator.FindLauncher();
            var asset = PickInstallerAsset(doc.RootElement, version, launcher);
            if (asset is null)
                return null;

            return (version!, asset.Value.Url, asset.Value.Name);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "VOICEVOX latest release fetch failed");
            return null;
        }
    }

    private static (string Url, string Name)? PickInstallerAsset(JsonElement release, string version, string? launcherPath)
    {
        if (!release.TryGetProperty("assets", out var assets))
            return null;

        var names = new List<(string Url, string Name)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.Contains("Web.Setup", StringComparison.OrdinalIgnoreCase))
                continue;
            names.Add((url, name));
        }

        if (names.Count == 0)
            return null;

        var preferCuda = launcherPath?.Contains("cuda", StringComparison.OrdinalIgnoreCase) == true
            || launcherPath?.Contains("nvidia", StringComparison.OrdinalIgnoreCase) == true;
        var preferCpu = launcherPath?.Contains("cpu", StringComparison.OrdinalIgnoreCase) == true
            && !preferCuda;

        string Pick(Func<string, bool> pred) =>
            names.FirstOrDefault(a => pred(a.Name)).Name;

        string? chosenName = null;
        if (preferCuda)
            chosenName = Pick(n => n.Contains("CUDA", StringComparison.OrdinalIgnoreCase));
        else if (preferCpu)
            chosenName = Pick(n => n.Contains("CPU", StringComparison.OrdinalIgnoreCase));
        else
            chosenName = Pick(n =>
                n.Contains("Web.Setup", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("CUDA", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("CPU", StringComparison.OrdinalIgnoreCase));

        chosenName ??= names[0].Name;
        var hit = names.First(a => string.Equals(a.Name, chosenName, StringComparison.OrdinalIgnoreCase));
        return (hit.Url, hit.Name);
    }

    private async Task<string?> DownloadInstallerAsync(string url, string fileName, CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalCompanion", "voicevox-update");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);

            using var resp = await _github.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(path);
            await input.CopyToAsync(output, ct);
            return path;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "VOICEVOX installer download failed");
            return null;
        }
    }

    /// <summary>本体の版があればそれを使い、無いときだけエンジンの版を使います。</summary>
    internal static string? SelectInstalledVersion(string? editorVersion, string? engineVersion)
    {
        var editor = NormalizeVersion(editorVersion);
        if (!string.IsNullOrWhiteSpace(editor))
            return editor;
        return NormalizeVersion(engineVersion);
    }

    /// <summary>インストール済みの版が、公開されている版以上なら true。</summary>
    internal static bool IsReleaseInstalled(string? target, string? installed)
    {
        var want = NormalizeVersion(target);
        var have = NormalizeVersion(installed);
        if (string.IsNullOrWhiteSpace(want) || string.IsNullOrWhiteSpace(have))
            return false;
        return !IsNewerVersion(want, have);
    }

    private async Task<string?> CurrentInstalledVersionAsync(CancellationToken ct)
    {
        var editor = ReadEditorVersion();
        if (!string.IsNullOrWhiteSpace(editor))
            return editor;

        var status = await _lifecycle.GetStatusAsync(ct);
        return SelectInstalledVersion(null, status.Version);
    }

    private string? ReadEditorVersion()
    {
        var path = _locator.FindEditor();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var raw = string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
            return NormalizeVersion(raw);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "VOICEVOX editor version unreadable");
            return null;
        }
    }

    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var v = raw.Trim().Trim('"').TrimStart('v');
        var dash = v.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
            v = v[..dash];
        return v;
    }

    internal static bool IsNewerVersion(string? latest, string? current)
    {
        if (string.IsNullOrWhiteSpace(latest))
            return false;
        if (string.IsNullOrWhiteSpace(current))
            return true;

        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;

        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
