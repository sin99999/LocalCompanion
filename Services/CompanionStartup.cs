using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>llama-server と初回 DL（GGUF / tools）。Web サーバーは起動しない。</summary>
public static class CompanionStartup
{
    private static readonly object StartupProgressOwner = new();

    public static async Task RunAsync(Action<StartupProgressReport> reportProgress, CancellationToken ct = default)
    {
        StartupProgressScope? progressScope = null;
        try
        {
            progressScope = StartupProgressScope.Acquire(StartupProgressOwner, reportProgress);
            var paths = AppPaths.Current;
            AppBootstrap.RegisterShutdown(paths);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    Shutdown();
                }
                catch
                {
                    /* 終了処理中の例外は無視 */
                }
            };

            var loc = LocalizationService.Instance;
            StartupProgress.ReportKey("Startup.Preparing", 0);
            AppBootstrap.StopExistingUiListeners();

            StartupProgress.ReportKey("Startup.ModelsFolder", 2, paths.ModelsDirectory);
            StartupProgress.ReportKey("Startup.LlamaPrepare", 5);

            var llamaCode = await Task.Run(() => AppBootstrap.EnsureLlamaServer(paths), ct).ConfigureAwait(true);
            if (llamaCode != 0)
                throw new InvalidOperationException(loc.Get("Startup.LlamaFailed"));

            StartupProgress.ReportKey("Startup.Ready", 100);

            if (AppServices.Provider is not null)
                AppServices.Get<VoicevoxLifecycleService>().EnsureInBackground();
        }
        finally
        {
            progressScope?.Dispose();
        }
    }

    public static void Shutdown() => ShutdownCore();

    /// <summary>ウィンドウを閉じたあと、UI をブロックせずバックグラウンドで終了処理する。</summary>
    private static int _shutdownStarted;

    public static void ShutdownInBackground()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;
        _ = Task.Run(ShutdownCore);
    }

    private static void ShutdownCore()
    {
        AppBootstrap.StopManagedLlamaOnExit();

        if (AppServices.Provider is not null)
        {
            try
            {
                AppServices.Get<VoicevoxLifecycleService>().StopManagedEngineOnExit();
            }
            catch
            {
                /* 終了処理中の DI 失敗は無視 */
            }
        }
    }
}
