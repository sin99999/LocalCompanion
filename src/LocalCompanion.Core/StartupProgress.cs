using LocalCompanion.Localization;

namespace LocalCompanion;

/// <summary>起動スプラッシュへ進捗を渡す（UI スレッドへは呼び出し側でマーシャル）。</summary>
public readonly record struct StartupProgressReport(string Message, double? Percent);

public static class StartupProgress
{
    public static Action<StartupProgressReport>? Handler { get; set; }

    private static string? _activeKey;
    private static object[] _activeArgs = Array.Empty<object>();
    private static double? _activePercent;
    private static string? _downloadLabelKey;
    private static object[] _downloadLabelArgs = Array.Empty<object>();

    public static void Report(string message, double? percent = null)
    {
        ClearActiveReport();
        Handler?.Invoke(new StartupProgressReport(message, percent));
    }

    public static void ReportKey(string key, double? percent = null, params object[] args)
    {
        _downloadLabelKey = null;
        _downloadLabelArgs = Array.Empty<object>();
        _activeKey = key;
        _activeArgs = args;
        _activePercent = percent;
        Handler?.Invoke(new StartupProgressReport(LocalizationService.Instance.Format(key, args), percent));
    }

    public static void ReportDownloadKey(
        string progressKey,
        double? percent,
        string labelKey,
        object[] labelArgs,
        params object[] progressArgs)
    {
        _activeKey = progressKey;
        _downloadLabelKey = labelKey;
        _downloadLabelArgs = labelArgs;
        _activeArgs = progressArgs;
        _activePercent = percent;
        Handler?.Invoke(new StartupProgressReport(FormatActiveMessage(), percent));
    }

    public static void RefreshForLanguage()
    {
        if (_activeKey is null || Handler is null)
            return;

        Handler.Invoke(new StartupProgressReport(FormatActiveMessage(), _activePercent));
    }

    private static string FormatActiveMessage()
    {
        if (_activeKey is null)
            return string.Empty;

        if (_downloadLabelKey is not null)
        {
            var label = LocalizationService.Instance.Format(_downloadLabelKey, _downloadLabelArgs);
            var args = new object[_activeArgs.Length + 1];
            args[0] = label;
            Array.Copy(_activeArgs, 0, args, 1, _activeArgs.Length);
            return LocalizationService.Instance.Format(_activeKey, args);
        }

        return LocalizationService.Instance.Format(_activeKey, _activeArgs);
    }

    private static void ClearActiveReport()
    {
        _activeKey = null;
        _activeArgs = Array.Empty<object>();
        _activePercent = null;
        _downloadLabelKey = null;
        _downloadLabelArgs = Array.Empty<object>();
    }

    internal static void ReportFromLog(string message)
    {
        var display = message;
        if (message.Length >= 4 && message[0] == '[')
        {
            var close = message.IndexOf(']');
            if (close > 0)
                display = message[(close + 1)..].Trim();
        }

        ClearActiveReport();
        Handler?.Invoke(new StartupProgressReport(display, null));
    }
}
