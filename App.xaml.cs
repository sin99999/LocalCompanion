using LocalCompanion.Localization;
using LocalCompanion.Services;
using Microsoft.UI.Xaml;

namespace LocalCompanion;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        RequestedTheme = AppAppearanceService.ResolveApplicationTheme(
            AppSettingsStore.ReadThemeModeForStartup());
        InitializeComponent();

        // 未処理例外はログへ残し、利用者が報告できるようにする（startup.log と同じフォルダー）。
        UnhandledException += (_, e) =>
        {
            StartupLog.Write(e.Exception, "UnhandledException");
            try
            {
                CompanionStartup.Shutdown();
            }
            catch
            {
                /* 終了処理中の例外は無視 */
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                StartupLog.Write(ex, "AppDomain.UnhandledException");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupLog.Write(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Configure();
        WinUiLanguageBridge.ApplyFromLocalization();
        LocalizationService.Instance.Changed += (_, _) => WinUiLanguageBridge.ApplyFromLocalization();
        AppServices.Get<AppAppearanceService>().ReloadFromStore();
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}
