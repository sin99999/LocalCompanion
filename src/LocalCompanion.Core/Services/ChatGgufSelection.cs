namespace LocalCompanion.Services;

/// <summary>フォルダ内のチャット用 GGUF から起動候補を選ぶ。</summary>
internal static class ChatGgufSelection
{
    internal static (string FileName, string FullPath)? PickLightest(string folder)
    {
        var candidates = ListChatGguf(folder);
        return candidates.Count == 0 ? null : PickLightest(candidates);
    }

    internal static (string FileName, string FullPath) PickLightest(
        IReadOnlyList<(string FileName, string FullPath)> candidates)
    {
        return candidates
            .OrderBy(c => GetSizeBytes(c.FullPath))
            .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    internal static IReadOnlyList<(string FileName, string FullPath)> ListChatGguf(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<(string, string)>();

        return Directory.EnumerateFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly)
            .Select(f => (FileName: Path.GetFileName(f), FullPath: f))
            .Where(x => !ModelCatalogService.IsMmprojFile(x.FileName))
            .ToList();
    }

    private static long GetSizeBytes(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).Length;
        }
        catch
        {
            return long.MaxValue;
        }
    }
}
