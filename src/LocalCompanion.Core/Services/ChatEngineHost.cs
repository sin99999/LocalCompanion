using System.Diagnostics;

namespace LocalCompanion.Services;

/// <summary>Strata を立てるときだけ使う。llama-server は記録した PID だけ止める。</summary>
public static class ChatEngineHost
{
    private static int _started;

    public static int StartAfterStoppingManagedLlama(AppPaths paths, ChatEnginePlan plan)
    {
        if (!plan.StartStrata)
            return 1;

        ManagedLlamaProcess.StopManaged(paths.ToolsDirectory, waitAfterKill: true, requireMarker: false);
        var exe = ChatEnginePlan.FindExecutable();
        if (exe == null)
            return 1;

        var start = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in plan.StrataArguments)
            start.ArgumentList.Add(arg);

        Process.Start(start);
        Interlocked.Exchange(ref _started, 1);
        return WaitUntilHealthy();
    }

    public static void StopIfStarted()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        var exe = ChatEnginePlan.FindExecutable();
        if (exe == null)
            return;

        var stop = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in ChatEnginePlan.StopArguments())
            stop.ArgumentList.Add(arg);

        try
        {
            Process.Start(stop);
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
                if (body.Contains("ok", StringComparison.OrdinalIgnoreCase))
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
