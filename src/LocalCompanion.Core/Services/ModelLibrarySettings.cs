using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>
/// 付属 models フォルダに加えて参照する「追加モデルフォルダ」の設定。
/// 設定はユーザーデータ領域（data）に保存し、追加フォルダ自体には書き込まない。
/// </summary>
public static class ModelLibrarySettings
{
    private const string FileName = "model-library.json";

    internal sealed record ModelLibraryDocument(string? AdditionalModelsFolder, bool FirstRunSetupComplete);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string SettingsPath(string dataDir) => Path.Combine(dataDir, FileName);

    internal static ModelLibraryDocument? TryLoadDocument(string dataDir)
    {
        var path = SettingsPath(dataDir);
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string? folder = null;
            if (root.TryGetProperty("additionalModelsFolder", out var folderEl))
                folder = folderEl.GetString();
            else if (root.TryGetProperty("AdditionalModelsFolder", out var legacyFolderEl))
                folder = legacyFolderEl.GetString();

            var complete = root.TryGetProperty("firstRunSetupComplete", out var completeEl)
                           && completeEl.ValueKind == JsonValueKind.True;
            return new ModelLibraryDocument(folder, complete);
        }
        catch
        {
            return null;
        }
    }

    internal static void SaveDocument(string dataDir, ModelLibraryDocument doc)
    {
        Directory.CreateDirectory(dataDir);
        var normalized = string.IsNullOrWhiteSpace(doc.AdditionalModelsFolder)
            ? null
            : Path.GetFullPath(doc.AdditionalModelsFolder.Trim());
        var payload = new
        {
            additionalModelsFolder = normalized,
            firstRunSetupComplete = doc.FirstRunSetupComplete,
        };
        AtomicFile.WriteAllText(SettingsPath(dataDir), JsonSerializer.Serialize(payload, JsonOpts));
    }

    /// <summary>追加モデルフォルダ（未設定・存在しない場合は null）。</summary>
    public static string? LoadAdditionalFolder(string dataDir)
    {
        var folder = TryLoadDocument(dataDir)?.AdditionalModelsFolder;
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        try
        {
            return Path.GetFullPath(folder.Trim());
        }
        catch
        {
            return null;
        }
    }

    public static void SaveAdditionalFolder(string dataDir, string? folder)
    {
        var existing = TryLoadDocument(dataDir);
        SaveDocument(dataDir, new ModelLibraryDocument(folder, existing?.FirstRunSetupComplete ?? false));
    }

    /// <summary>
    /// スキャン対象のモデルフォルダ一覧。先頭が付属 models（優先）、続いて追加フォルダ。
    /// 重複・存在しないフォルダは除外する。
    /// </summary>
    public static IReadOnlyList<string> EnumerateModelFolders(string primaryModelsDir, string dataDir)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            string full;
            try { full = Path.GetFullPath(dir); }
            catch { return; }
            if (!Directory.Exists(full))
                return;
            if (seen.Add(full))
                result.Add(full);
        }

        Add(primaryModelsDir);
        Add(LoadAdditionalFolder(dataDir));
        return result;
    }
}
