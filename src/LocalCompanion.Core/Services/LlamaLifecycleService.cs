using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Services;

/// <summary>LocalCompanion 経由で起動した llama-server の終了管理。</summary>
public sealed class LlamaLifecycleService
{
    private readonly string _root;
    private readonly string _markerPath;
    private readonly AppPaths _paths;

    public LlamaLifecycleService(AppPaths paths)
    {
        _paths = paths;
        _root = paths.Root;
        _markerPath = LlamaManagedMarker.ResolvePath(paths.ToolsDirectory);
    }

    public bool StopIfManaged()
    {
        if (!LlamaManagedMarker.IsActive(_markerPath))
            return false;

        var stopped = StopAllLlamaServers();

        try { File.Delete(_markerPath); } catch { /* ignore */ }

        return stopped;
    }

    /// <summary>マーカー不整合時の保険として llama-server を強制停止する。</summary>
    public bool ForceStopAll()
    {
        var stopped = StopAllLlamaServers();
        try { File.Delete(_markerPath); } catch { /* ignore */ }
        return stopped;
    }

    private static bool StopAllLlamaServers()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("llama-server");
        if (procs.Length == 0)
            return false;

        foreach (var proc in procs)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // 既に終了している場合など
            }
            finally
            {
                proc.Dispose();
            }
        }

        // すぐ再起動すると競合しやすいので、終了を少し待つ
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (System.Diagnostics.Process.GetProcessesByName("llama-server").Length == 0)
                break;
            Thread.Sleep(150);
        }
        return true;
    }

    public bool IsManagedMarkerPresent() => LlamaManagedMarker.IsActive(_markerPath);

    public static bool IsLlamaProcessRunning()
        => System.Diagnostics.Process.GetProcessesByName("llama-server").Length > 0;

    /// <summary>モデル変更後などに llama-server をバックグラウンド起動する。</summary>
    public bool TryStartManagedInBackground()
    {
        try
        {
            _ = Task.Run(() => LlamaServerNativeHost.EnsureAndStart(_paths));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
