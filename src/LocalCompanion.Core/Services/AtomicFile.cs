namespace LocalCompanion.Services;

/// <summary>設定 JSON 等の原子的書き込み（クラッシュ時の破損を防ぐ）。</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
            throw;
        }
    }
}
