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
    private bool _warmedUp;
    private volatile bool _updateInProgress;

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
    public bool StopEngineProcessesForUpdate() => StopEngineProcesses(engineOnly: false);

    /// <summary>このセッションで自動起動した run.exe のみ停止（アプリ終了時）。</summary>
    public void StopManagedEngineOnExit()
    {
        if (!_startAttempted)
            return;

        StopEngineProcesses(engineOnly: true);
        ResetStartState();
    }

    private bool StopEngineProcesses(bool engineOnly)
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

                if (engineOnly && !path.EndsWith("run.exe", StringComparison.OrdinalIgnoreCase))
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

    public void EnsureInBackground()
    {
        if (_updateInProgress || !_opt.AutoStart || !_locator.IsInstalled)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
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
        if (!_locator.IsInstalled)
            return new VoicevoxStatusDto(false, false, false, _client.BaseUrl, null, null);

        var current = await _client.GetStatusAsync(ct);
        if (current.Available)
        {
            if (!_updateInProgress)
            {
                _settings.ApplyFirstRunDefaultsIfNeeded();
                WarmUpEngineOnce();
            }

            return current with { Installed = true, ManagedByApp = false };
        }

        if (_updateInProgress || !_opt.AutoStart)
            return new VoicevoxStatusDto(false, true, false, _client.BaseUrl, null, null);

        lock (_startLock)
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

        var deadline = DateTime.UtcNow.AddSeconds(_opt.StartupWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);
            var probe = await _client.GetStatusAsync(ct);
            if (probe.Available)
            {
                _settings.ApplyFirstRunDefaultsIfNeeded();
                WarmUpEngineOnce();
                _log.LogInformation("VOICEVOX engine ready");
                return probe with { Installed = true, ManagedByApp = true, Hint = LocalizationService.Instance.Get("Voicevox.Status.ReadyAutoStart") };
            }
        }

        _log.LogDebug("VOICEVOX installed but engine not ready yet");
        return new VoicevoxStatusDto(false, true, true, _client.BaseUrl, null, null);
    }

    private void TryStartProcess()
    {
        var exe = _locator.FindLauncher();
        if (exe is null)
            return;

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

            System.Diagnostics.Process.Start(psi);
            _log.LogInformation("VOICEVOX launch attempted: {Exe}", exe);
        }
        catch (Exception ex)
        {
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
        if (_updateInProgress || _warmedUp)
            return;
        _warmedUp = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await _client.SynthesizeAsync("。", _settings.Load(), autoSpeak: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "VOICEVOX warm-up skipped");
            }
        });
    }
}
