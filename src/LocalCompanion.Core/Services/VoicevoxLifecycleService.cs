using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class VoicevoxLifecycleService
{
    private readonly VoicevoxClient _client;
    private readonly VoicevoxInstallLocator _locator;
    private readonly VoicevoxSettingsStore _settings;
    private readonly VoicevoxOptions _opt;
    private readonly ILogger<VoicevoxLifecycleService> _log;
    private readonly object _startLock = new();
    private bool _startAttempted;
    /// <summary>Process.Start に成功したら true（タイムアウト後も終了時停止の対象にする）。</summary>
    private bool _managedProcessStarted;
    private bool _warmedUp;
    private volatile bool _updateInProgress;
    private int? _managedProcessId;
    private string? _managedLauncherPath;

    public bool IsUpdateInProgress => _updateInProgress;

    public VoicevoxLifecycleService(
        VoicevoxClient client,
        VoicevoxInstallLocator locator,
        VoicevoxSettingsStore settings,
        IOptions<VoicevoxOptions> opt,
        ILogger<VoicevoxLifecycleService> log)
    {
        _client = client;
        _locator = locator;
        _settings = settings;
        _opt = opt.Value;
        _log = log;
    }

    public bool IsInstalled => _locator.IsInstalled;

    public void BeginUpdate() => _updateInProgress = true;

    public void EndUpdate()
    {
        _updateInProgress = false;
        ResetStartState();
    }

    /// <summary>更新前に VOICEVOX エンジン／本体プロセスを停止する（ファイルロック回避）。</summary>
    public bool StopEngineProcessesForUpdate() => StopEngineProcesses(engineOnly: false, managedLauncherPath: null);

    private bool StopEngineProcesses(bool engineOnly, string? managedLauncherPath)
    {
        var roots = _locator.GetInstallRootPaths();
        var stopped = false;

        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (proc.HasExited)
                    continue;

                if (!TryGetProcessPath(proc, out var path) || !IsVoicevoxProcessPath(path, roots))
                    continue;

                if (engineOnly && !ShouldStopProcessOnAppExit(path, managedLauncherPath))
                    continue;

                proc.Kill(entireProcessTree: true);
                stopped = true;
                _log.LogInformation(
                    "VOICEVOX process stopped ({Mode}): {Path}",
                    engineOnly ? "exit" : "update",
                    path);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "VOICEVOX process stop skipped (pid={Pid})", proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }

        if (!engineOnly)
            ResetStartState();

        return stopped;
    }

    /// <summary>終了時に止めてよい VOICEVOX プロセスか。このセッションで起動したものだけ。</summary>
    internal static bool ShouldStopProcessOnAppExit(string processPath, string? managedLauncherPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        if (string.IsNullOrWhiteSpace(managedLauncherPath))
            return false;

        try
        {
            var managedFull = Path.GetFullPath(managedLauncherPath);
            var processFull = Path.GetFullPath(processPath);
            if (string.Equals(processFull, managedFull, StringComparison.OrdinalIgnoreCase))
                return true;

            // run.exe / ENGINE.exe が同じインストールフォルダにあるときだけ兄弟も止める
            var managedDir = Path.GetDirectoryName(managedFull);
            var processDir = Path.GetDirectoryName(processFull);
            if (string.IsNullOrEmpty(managedDir) || string.IsNullOrEmpty(processDir))
                return false;

            if (!string.Equals(managedDir, processDir, StringComparison.OrdinalIgnoreCase))
                return false;

            return processFull.EndsWith("run.exe", StringComparison.OrdinalIgnoreCase)
                || processFull.EndsWith("ENGINE.exe", StringComparison.OrdinalIgnoreCase)
                || processFull.EndsWith("VOICEVOX.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>このセッションで自動起動したエンジンを停止（アプリ終了時）。</summary>
    public void StopManagedEngineOnExit()
    {
        string? launcher;
        lock (_startLock)
        {
            if (!_startAttempted && !_managedProcessStarted && _managedProcessId is null)
                return;
            launcher = _managedLauncherPath;
        }

        StopTrackedManagedProcess();
        StopEngineProcesses(engineOnly: true, launcher);
        ResetStartState();
    }

    private void StopTrackedManagedProcess()
    {
        int? pid;
        string? launcher;
        lock (_startLock)
        {
            pid = _managedProcessId;
            launcher = _managedLauncherPath;
        }

        if (pid is not int id)
            return;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(id);
            if (proc.HasExited)
                return;

            if (!TryGetProcessPath(proc, out var path))
                return;

            var roots = _locator.GetInstallRootPaths();
            if (!IsVoicevoxProcessPath(path, roots) && !ShouldStopProcessOnAppExit(path, launcher))
                return;

            proc.Kill(entireProcessTree: true);
            _log.LogInformation("VOICEVOX managed process stopped (pid={Pid}): {Path}", id, path);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "VOICEVOX managed pid stop skipped (pid={Pid})", id);
        }
    }

    public void EnsureInBackground()
    {
        if (AppBootstrap.IsExitRequested || _updateInProgress || !_opt.AutoStart || !_locator.IsInstalled)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (AppBootstrap.IsExitRequested)
                    return;
                await EnsureRunningAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "VOICEVOX background ensure failed");
            }
        });
    }

    public async Task<VoicevoxStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        if (!_locator.IsInstalled)
            return new VoicevoxStatusDto(false, false, false, _client.BaseUrl, null, null);

        var live = await _client.GetStatusAsync(ct);
        if (live.Available)
        {
            WarmUpEngineOnce();
            return live with { Installed = true };
        }

        return new VoicevoxStatusDto(
            false,
            true,
            false,
            _client.BaseUrl,
            null,
            LocalizationService.Instance.Get("Voicevox.Status.WaitingStart"));
    }

    public async Task<VoicevoxStatusDto> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (AppBootstrap.IsExitRequested)
            return new VoicevoxStatusDto(false, _locator.IsInstalled, false, _client.BaseUrl, null, null);

        if (!_locator.IsInstalled)
            return new VoicevoxStatusDto(false, false, false, _client.BaseUrl, null, null);

        var current = await _client.GetStatusAsync(ct);
        if (current.Available)
        {
            if (!_updateInProgress && !AppBootstrap.IsExitRequested)
            {
                _settings.ApplyFirstRunDefaultsIfNeeded();
                WarmUpEngineOnce();
            }

            return current with { Installed = true, ManagedByApp = false };
        }

        if (AppBootstrap.IsExitRequested || _updateInProgress || !_opt.AutoStart)
            return new VoicevoxStatusDto(false, true, false, _client.BaseUrl, null, null);

        lock (_startLock)
        {
            if (!AppBootstrap.IsExitRequested)
            {
                if (_startAttempted)
                {
                    /* 同一セッションで二重起動しない */
                }
                else
                {
                    _startAttempted = true;
                    TryStartProcess();
                }
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(_opt.StartupWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (AppBootstrap.IsExitRequested)
                return new VoicevoxStatusDto(false, true, true, _client.BaseUrl, null, null);

            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);
            var probe = await _client.GetStatusAsync(ct);
            if (probe.Available)
            {
                if (AppBootstrap.IsExitRequested)
                    return new VoicevoxStatusDto(false, true, true, _client.BaseUrl, null, null);

                _settings.ApplyFirstRunDefaultsIfNeeded();
                WarmUpEngineOnce();
                _log.LogInformation("VOICEVOX engine ready");
                return probe with { Installed = true, ManagedByApp = true, Hint = LocalizationService.Instance.Get("Voicevox.Status.ReadyAutoStart") };
            }
        }

        if (!AppBootstrap.IsExitRequested)
        {
            lock (_startLock)
                _startAttempted = false;
        }
        _log.LogDebug("VOICEVOX installed but engine not ready yet; will retry next ensure");
        return new VoicevoxStatusDto(false, true, true, _client.BaseUrl, null, null);
    }

    private void TryStartProcess()
    {
        if (AppBootstrap.IsExitRequested)
            return;

        var exe = _locator.FindLauncher();
        if (exe is null)
        {
            _startAttempted = false;
            return;
        }

        try
        {
            var port = ResolvePort();
            var isEngine = exe.EndsWith("run.exe", StringComparison.OrdinalIgnoreCase);
            var args = isEngine
                ? $"--host 127.0.0.1 --port {port}"
                : "";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };

            var started = System.Diagnostics.Process.Start(psi);
            if (started is not null)
            {
                ChildProcessJob.Assign(started);
                _managedProcessId = started.Id;
                _managedLauncherPath = exe;
                _managedProcessStarted = true;
                _log.LogInformation("VOICEVOX launch attempted: {Exe}", exe);
            }
            else
            {
                _startAttempted = false;
                _log.LogDebug("VOICEVOX Process.Start returned null: {Exe}", exe);
            }
        }
        catch (Exception ex)
        {
            _startAttempted = false;
            _log.LogDebug(ex, "VOICEVOX launch failed");
        }
    }

    private int ResolvePort()
    {
        if (Uri.TryCreate(_client.BaseUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
            return uri.Port;
        return 50021;
    }

    private void ResetStartState()
    {
        lock (_startLock)
        {
            _startAttempted = false;
            _managedProcessStarted = false;
            _managedProcessId = null;
            _managedLauncherPath = null;
        }

        _warmedUp = false;
    }

    private static bool TryGetProcessPath(System.Diagnostics.Process proc, out string path)
    {
        path = "";
        try
        {
            var main = proc.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(main))
                return false;
            path = main;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVoicevoxProcessPath(string path, IReadOnlyList<string> roots)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in roots)
        {
            if (full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return full.Contains(@"\VOICEVOX\", StringComparison.OrdinalIgnoreCase)
            || full.Contains(@"\VOICEVOX ENGINE\", StringComparison.OrdinalIgnoreCase);
    }

    private void WarmUpEngineOnce()
    {
        if (AppBootstrap.IsExitRequested || _updateInProgress || _warmedUp)
            return;
        _warmedUp = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (AppBootstrap.IsExitRequested)
                    return;
                await _client.SynthesizeAsync("。", _settings.Load(), autoSpeak: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "VOICEVOX warm-up skipped");
            }
        });
    }
}
