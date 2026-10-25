using System.Diagnostics;

namespace LocalCompanion.Services;

/// <summary>Strata を立てるときだけ使う。llama-server は記録した PID だけ止める。</summary>
public static class ChatEngineHost
{
    private static int _started;

    public static bool IsRunning => Volatile.Read(ref _started) == 1;

    public static int StartAfterStoppingManagedLlama(AppPaths paths, ChatEnginePlan plan)
    {
        if (!plan.StartStrata)
            return 1;

        var exe = ChatEnginePlan.FindExecutable();
        if (exe == null)
            return 1;

        var start = CreateLaunchInfo(exe, plan.StrataArguments);
        ManagedLlamaProcess.StopManaged(paths.ToolsDirectory, waitAfterKill: true, requireMarker: false);
        Process.Start(start);
        Interlocked.Exchange(ref _started, 1);
        var code = WaitUntilHealthy();
        if (code != 0)
            StopIfStarted();
        return code;
    }

    public static ProcessStartInfo CreateLaunchInfo(string executable, IReadOnlyList<string> args)
    {
        var extension = Path.GetExtension(executable);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var command = QuoteArg(executable);
            foreach (var arg in args)
                command += " " + QuoteArg(arg);
            return new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /s /c \"" + command + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return start;
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    public static void StopIfStarted()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        var exe = ChatEnginePlan.FindExecutable();
        if (exe == null)
            return;

        try
        {
            Process.Start(CreateLaunchInfo(exe, ChatEnginePlan.StopArguments()));
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Strata stop failed");
        }
    }

    private static int WaitUntilHealthy()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var url = $"http://{ChatEnginePlan.ListenHost}:{ChatEnginePlan.ListenPort}/health";
        var deadline = DateTime.UtcNow.AddMinutes(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var body = http.GetStringAsync(url).GetAwaiter().GetResult();
                if (body.Contains("\"loaded\": true", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("\"loaded\":true", StringComparison.OrdinalIgnoreCase))
                    return 0;
            }
            catch
            {
                /* モデル取得中はまだ応答しない */
            }

            Thread.Sleep(TimeSpan.FromSeconds(5));
        }

        StartupLog.Write("Strata health wait timed out");
        return 1;
    }
}
