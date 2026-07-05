using System.Security.Cryptography;
using System.Text;

namespace LocalCompanion.Services;

/// <summary>AI 構造化結果をユーザーデータ配下にキャッシュ（元ファイルは変更しない）。</summary>
internal sealed class RagStructurerCache
{
    private readonly string _cacheDir;

    public RagStructurerCache(string userDataDirectory)
    {
        _cacheDir = Path.Combine(userDataDirectory, "rag-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public string? TryLoad(string source, string rawText)
    {
        var path = ResolvePath(source, rawText);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    public void Save(string source, string rawText, string structuredText)
    {
        if (string.IsNullOrWhiteSpace(structuredText))
            return;

        var path = ResolvePath(source, rawText);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, structuredText);
    }

    private string ResolvePath(string source, string rawText)
    {
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(source + "\0" + rawText.Length.ToString())));
        var baseName = SanitizeFileName(Path.GetFileName(source));
        return Path.Combine(_cacheDir, $"{baseName}_{key[..16]}.md");
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "document";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 80 ? name[..80] : name;
    }
}
