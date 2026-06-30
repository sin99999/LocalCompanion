using System.Diagnostics;
using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion;

/// <summary>ネイティブ exe 起動時の前処理（llama-server 起動・既存 UI の掃除）。</summary>
public static class AppBootstrap
{
    public const int UiPort = 5781;

    private static AppPaths? _shutdownPaths;
    private static int _stopInvoked;

    public static void RegisterShutdown(AppPaths paths)
    {
        _shutdownPaths = paths;
    }

    /// <summary>アプリ終了時に llama-server を止める（複数回呼んでも安全）。</summary>
    public static void StopManagedLlamaOnExit()
    {
        if (Interlocked.Exchange(ref _stopInvoked, 1) != 0)
            return;

        var paths = _shutdownPaths ?? AppPaths.Current;
        try
        {
            StopManagedLlama(paths, waitForLlamaExit: false);
            StartupLog.Write("StopManagedLlamaOnExit done");
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "StopManagedLlamaOnExit");
        }
    }

    public static void StopExistingUiListeners()
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            try { currentPath = Process.GetCurrentProcess().MainModule?.FileName; }
            catch { /* ignore */ }
        }

        var killed = false;
        foreach (var proc in Process.GetProcessesByName("LocalCompanion"))
        {
            try
            {
                if (proc.Id == Environment.ProcessId)
                    continue;

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    string? otherPath = null;
                    try { otherPath = proc.MainModule?.FileName; }
                    catch { /* access denied etc. */ }

                    if (!string.Equals(otherPath, currentPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    killed = true;
                }
            }
            catch { /* ignore */ }
            finally
            {
                proc.Dispose();
            }
        }

        if (killed)
            Thread.Sleep(2000);
    }

    public static int EnsureLlamaServer(AppPaths paths) =>
        LlamaServerNativeHost.EnsureAndStart(paths);

    public static void StopManagedLlama(AppPaths paths, bool waitForLlamaExit = true)
    {
        var toolsDir = paths.ToolsDirectory;
        var managed = LlamaManagedMarker.IsActiveInToolsDir(toolsDir);

        if (managed)
            LlamaServerNativeHost.StopLlamaProcesses(toolsDir, waitForLlamaExit);
    }
}
