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
        => ManagedLlamaProcess.StopManaged(_paths.ToolsDirectory, waitAfterKill: true, requireMarker: true);

    /// <summary>モデル適用など。記録 PID があれば停止（他アプリの llama-server は対象外）。</summary>
    public bool ForceStopAll()
        => ManagedLlamaProcess.StopManaged(_paths.ToolsDirectory, waitAfterKill: true, requireMarker: false);

    public bool IsManagedMarkerPresent() => LlamaManagedMarker.IsActive(_markerPath);

    public bool IsManagedLlamaRunning()
    {
        var pid = ManagedLlamaProcess.TryReadPid(_paths.ToolsDirectory);
        if (pid is not int trackedPid)
            return false;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(trackedPid);
            return !proc.HasExited
                   && proc.ProcessName.Equals("llama-server", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

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
