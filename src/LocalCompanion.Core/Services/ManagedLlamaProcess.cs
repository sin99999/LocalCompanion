using System.Diagnostics;

namespace LocalCompanion.Services;

/// <summary>LocalCompanion が起動した llama-server の PID 追跡と限定停止。</summary>
public static class ManagedLlamaProcess
{
    public const string PidFileName = ".localcompanion-llama-pid";

    public static string ResolvePidPath(string toolsDir) => Path.Combine(toolsDir, PidFileName);

    public static void WritePid(string toolsDir, int pid)
    {
        Directory.CreateDirectory(toolsDir);
        File.WriteAllText(ResolvePidPath(toolsDir), pid.ToString());
    }

    public static int? TryReadPid(string toolsDir)
    {
        var path = ResolvePidPath(toolsDir);
        if (!File.Exists(path))
            return null;
        try
        {
            return int.TryParse(File.ReadAllText(path).Trim(), out var pid) && pid > 0 ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearTracking(string toolsDir)
    {
        try { File.Delete(ResolvePidPath(toolsDir)); } catch { /* ignore */ }
        try { File.Delete(LlamaManagedMarker.ResolvePath(toolsDir)); } catch { /* ignore */ }
        LlamaManagedMarker.RemoveLegacyMarkers(toolsDir);
    }

    /// <summary>管理マーカーまたは記録 PID に基づき、自アプリ起動分のみ停止する。</summary>
    public static bool StopManaged(string toolsDir, bool waitAfterKill = true, bool requireMarker = true)
    {
        var markerActive = LlamaManagedMarker.IsActiveInToolsDir(toolsDir);
        var pid = TryReadPid(toolsDir);
        if (requireMarker && !markerActive && pid is null)
            return false;

        var stopped = false;
        var trackedPid = pid ?? -1;
        if (pid is int p)
            stopped = TryKillLlamaPid(p);

        ClearTracking(toolsDir);

        if (waitAfterKill && stopped)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (!IsTrackedPidAlive(trackedPid))
                    break;
                Thread.Sleep(150);
            }
            Thread.Sleep(waitAfterKill ? 500 : 0);
        }

        return stopped;
    }

    private static bool TryKillLlamaPid(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.HasExited)
                return false;
            if (!proc.ProcessName.Equals("llama-server", StringComparison.OrdinalIgnoreCase))
                return false;
            proc.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTrackedPidAlive(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
