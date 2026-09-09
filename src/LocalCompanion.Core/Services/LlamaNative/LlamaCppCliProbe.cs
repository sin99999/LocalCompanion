using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalCompanion.Services.LlamaNative;

/// <summary>展開した llama-cli が Windows のローダで落ちないかを見る。</summary>
internal static class LlamaCppCliProbe
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    internal static bool CanStart(string workDir) =>
        TryRun(workDir, "--version", TimeSpan.FromSeconds(10), out var exit)
        && !LlamaCppLaunchFiles.IsWindowsLoaderFailure(exit);

    internal static bool TryRun(string workDir, string arguments, TimeSpan timeout, out int exitCode, out string output)
    {
        exitCode = -1;
        output = string.Empty;
        var cli = Path.Combine(workDir, "llama-cli.exe");
        if (!File.Exists(cli))
            return false;

        var previous = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
        try
        {
            var psi = new ProcessStartInfo(cli, arguments)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;

            output = proc.StandardOutput.ReadToEnd();
            output += proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            exitCode = proc.ExitCode;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            SetErrorMode(previous);
        }
    }

    internal static bool TryRun(string workDir, string arguments, TimeSpan timeout, out int exitCode)
    {
        var ok = TryRun(workDir, arguments, timeout, out exitCode, out _);
        return ok;
    }
}
