using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>llama-server と初回 DL（GGUF / tools）。Web サーバーは起動しない。</summary>
public static class CompanionStartup
{
    private static readonly object StartupProgressOwner = new();
    private static int _processExitHooked;

    public static void EnsureProcessExitHook()
    {
        if (Interlocked.Exchange(ref _processExitHooked, 1) == 0)
        {
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
        }
    }

    public static async Task RunAsync(Action<StartupProgressReport> reportProgress, CancellationToken ct = default)
    {
        StartupProgressScope? progressScope = null;
        try
        {
            progressScope = StartupProgressScope.Acquire(StartupProgressOwner, reportProgress);
            var paths = AppPaths.Current;
            AppBootstrap.RegisterShutdown(paths);
            EnsureProcessExitHook();

            var loc = LocalizationService.Instance;
            StartupProgress.ReportKey("Startup.Preparing", 0);
            AppBootstrap.StopExistingUiListeners();

            StartupProgress.ReportKey("Startup.ModelsFolder", 2, paths.ModelsDirectory);
            StartupProgress.ReportKey("Startup.LlamaPrepare", 5);

            var plan = ChatEnginePlan.ForInstalled(paths.Root);
            var fellBack = false;
            int llamaCode;
            if (plan.StartStrata)
            {
                llamaCode = await Task.Run(() => ChatEngineHost.StartAfterStoppingManagedLlama(paths, plan), ct).ConfigureAwait(true);
                if (llamaCode != 0)
                {
                    ChatEngineHost.StopIfStarted();
                    ChatEnginePlan.RevertSavedEngineToLlama(paths.Root);
                    fellBack = true;
                    llamaCode = await Task.Run(() => AppBootstrap.EnsureLlamaServer(paths), ct).ConfigureAwait(true);
                }
            }
            else
            {
                llamaCode = await Task.Run(() => AppBootstrap.EnsureLlamaServer(paths), ct).ConfigureAwait(true);
            }
            if (llamaCode == 2)
                throw new InvalidOperationException(loc.Get("Startup.PortInUseForeign"));
            if (llamaCode == 3)
                throw new InvalidOperationException(loc.Get("Startup.Error.DefaultModelDownload"));
            if (llamaCode == 1)
                throw new InvalidOperationException(loc.Get("Startup.Error.NoChatGguf"));
            if (llamaCode != 0)
                throw new InvalidOperationException(loc.Get("Startup.LlamaFailed"));

            if (fellBack)
                StartupProgress.ReportKey("Startup.StrataFellBack", 100);
            else if (plan.HoldStrataUntilDownload)
                StartupProgress.ReportKey("Startup.StrataHeld", 100);
            else
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
        ChatEngineHost.StopIfStarted();
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
