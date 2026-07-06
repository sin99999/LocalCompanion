using System.Text;

namespace LocalCompanion.Services;

/// <summary>デスクトップ等の書き込み可能な出力先を解決する。</summary>
internal static class DesktopPathResolver
{
    public static (string Directory, bool UsedFallback) ResolveDesktopOrFallback()
    {
        foreach (var candidate in EnumerateDesktopCandidates())
        {
            if (CanWriteToDirectory(candidate))
                return (candidate, false);
        }

        var fallback = Path.Combine(AppPaths.ResolveUserDataDirectory(null), "exports");
        Directory.CreateDirectory(fallback);
        return (fallback, true);
    }

    private static IEnumerable<string> EnumerateDesktopCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                 })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".lc-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok", new UTF8Encoding(false));
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
