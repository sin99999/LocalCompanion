namespace LocalCompanion;

internal static class StartupLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void ConfigureUserDataDirectory(string userDataDirectory)
    {
        Directory.CreateDirectory(userDataDirectory);
        _path = Path.Combine(userDataDirectory, "startup.log");
    }

    public static string LogPath
    {
        get
        {
            if (_path is not null)
                return _path;
            var dir = AppPaths.ResolveUserDataDirectory(null);
            ConfigureUserDataDirectory(dir);
            return _path!;
        }
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // ignore
            }
        }
    }

    public static void Write(Exception ex, string context)
    {
        Write($"{context}: {ex.GetType().Name}: {ex.Message}");
        Write(ex.StackTrace ?? "(no stack)");
    }
}
