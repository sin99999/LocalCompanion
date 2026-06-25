using LocalCompanion.Localization;

namespace LocalCompanion.Services.LlamaNative;

internal static class NativeLog
{
    internal static void Write(string message, double? percent = null)
    {
        try
        {
            StartupLog.Write(message);
            if (percent is not null)
                StartupProgress.Report(TrimLogPrefix(message), percent);
            else
                StartupProgress.ReportFromLog(message);
        }
        catch
        {
            /* ignore */
        }
    }

    internal static void WriteKey(string key, double? percent = null, params object[] args)
    {
        try
        {
            var text = LocalizationService.Instance.Format(key, args);
            StartupLog.Write($"[..] {text}");
            StartupProgress.ReportKey(key, percent, args);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>startup.log にだけ残す（スプラッシュには表示しない）。</summary>
    internal static void WriteKeyLogOnly(string key, params object[] args)
    {
        try
        {
            var text = LocalizationService.Instance.Format(key, args);
            StartupLog.Write($"[..] {text}");
        }
        catch
        {
            /* ignore */
        }
    }

    private static string TrimLogPrefix(string message)
    {
        if (message.Length >= 4 && message[0] == '[')
        {
            var close = message.IndexOf(']');
            if (close > 0)
                return message[(close + 1)..].Trim();
        }

        return message;
    }
}
