using LocalCompanion.Localization;

namespace LocalCompanion.Services.LlamaNative;

internal static class DownloadProgress
{
    internal static void CopyStream(
        Stream source,
        Stream dest,
        string labelKey,
        long? totalBytes = null,
        params object[] labelArgs)
    {
        if (totalBytes is null or <= 0 && source.CanSeek)
            totalBytes = source.Length;

        var buffer = new byte[81920];
        long totalRead = 0;
        var lastReport = DateTime.MinValue;

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            dest.Write(buffer, 0, read);
            totalRead += read;

            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 300)
                continue;

            lastReport = DateTime.UtcNow;
            var mbRead = totalRead / (1024.0 * 1024.0);
            if (totalBytes is > 0)
            {
                var pct = Math.Min(99, totalRead * 100.0 / totalBytes.Value);
                var mbTotal = totalBytes.Value / (1024.0 * 1024.0);
                StartupProgress.ReportDownloadKey(
                    "Startup.Download.WithTotal",
                    pct,
                    labelKey,
                    labelArgs,
                    mbRead,
                    mbTotal,
                    pct);
            }
            else
            {
                StartupProgress.ReportDownloadKey(
                    "Startup.Download.UnknownTotal",
                    null,
                    labelKey,
                    labelArgs,
                    mbRead);
            }
        }
    }
}
