namespace LocalCompanion.Services;

/// <summary>LocalCompanion 経由で起動した llama-server の管理マーカー。</summary>
public static class LlamaManagedMarker
{
    public const string FileName = ".localcompanion-managed";
    public const string LegacyFileName = ".legacy-managed";

    /// <summary>旧バージョンのマーカー名（互換のため読み取りのみ）。</summary>
    private const string ObsoleteLegacyFileName = ".new2-managed";

    private static readonly string[] LegacyFileNames = [LegacyFileName, ObsoleteLegacyFileName];

    public static string ResolvePath(string toolsDir)
    {
        Directory.CreateDirectory(toolsDir);
        var current = Path.Combine(toolsDir, FileName);
        if (File.Exists(current))
            return current;

        foreach (var legacyName in LegacyFileNames)
        {
            var legacy = Path.Combine(toolsDir, legacyName);
            if (!File.Exists(legacy))
                continue;

            try
            {
                File.Move(legacy, current);
                return current;
            }
            catch
            {
                return legacy;
            }
        }

        return current;
    }

    public static bool IsActive(string markerPath)
    {
        if (!File.Exists(markerPath))
            return false;
        try
        {
            return File.ReadAllText(markerPath).Trim() == "1";
        }
        catch
        {
            return false;
        }
    }

    public static bool IsActiveInToolsDir(string toolsDir)
    {
        var current = Path.Combine(toolsDir, FileName);
        if (IsActive(current))
            return true;

        foreach (var legacyName in LegacyFileNames)
        {
            if (IsActive(Path.Combine(toolsDir, legacyName)))
                return true;
        }

        return false;
    }

    public static void RemoveLegacyMarkers(string toolsDir)
    {
        foreach (var legacyName in LegacyFileNames)
        {
            try { File.Delete(Path.Combine(toolsDir, legacyName)); } catch { /* ignore */ }
        }
    }
}
