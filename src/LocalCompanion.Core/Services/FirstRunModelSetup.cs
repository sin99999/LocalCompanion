using System.Text.Json;
using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Services;

/// <summary>初回起動時のモデルセットアップ（言語選択の次）。</summary>
public static class FirstRunModelSetup
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static bool NeedsSetup(string dataDir, string modelsDir)
    {
        if (IsSetupComplete(dataDir))
            return false;

        if (HasLegacySetup(modelsDir, dataDir))
        {
            MarkSetupComplete(dataDir);
            return false;
        }

        return true;
    }

    public static void CompleteDefaultSetup(string dataDir) => MarkSetupComplete(dataDir);

    /// <summary>外部 GGUF フォルダを指定して初回セットアップを完了する。</summary>
    public static bool TryCompleteExternalFolder(
        string modelsDir,
        string dataDir,
        string folder,
        out string? errorKey)
    {
        errorKey = null;
        if (string.IsNullOrWhiteSpace(folder))
        {
            errorKey = "FirstRun.Setup.Error.InvalidFolder";
            return false;
        }

        string fullFolder;
        try
        {
            fullFolder = Path.GetFullPath(folder.Trim());
        }
        catch
        {
            errorKey = "FirstRun.Setup.Error.InvalidFolder";
            return false;
        }

        if (!Directory.Exists(fullFolder))
        {
            errorKey = "FirstRun.Setup.Error.InvalidFolder";
            return false;
        }

        var chosen = ChatGgufSelection.PickLightest(fullFolder);
        if (chosen is null)
        {
            errorKey = "FirstRun.Setup.Error.NoGguf";
            return false;
        }

        ModelLibrarySettings.SaveAdditionalFolder(dataDir, fullFolder);
        var root = Directory.GetParent(modelsDir)?.FullName ?? modelsDir;
        SaveSelection(modelsDir, chosen.Value.FileName, chosen.Value.FullPath, root);
        DefaultModelDownloader.MarkBootstrapSkipped(modelsDir, "external_folder");
        MarkSetupComplete(dataDir);
        return true;
    }

    private static bool IsSetupComplete(string dataDir)
    {
        var doc = TryLoadDocument(dataDir);
        return doc?.FirstRunSetupComplete == true;
    }

    private static void MarkSetupComplete(string dataDir)
    {
        var doc = TryLoadDocument(dataDir) ?? new ModelLibrarySettings.ModelLibraryDocument(null, false);
        ModelLibrarySettings.SaveDocument(dataDir, doc with { FirstRunSetupComplete = true });
    }

    private static bool HasLegacySetup(string modelsDir, string dataDir)
    {
        if (File.Exists(Path.Combine(modelsDir, ".default-model-bootstrap.json")))
            return true;

        var selectionPath = Path.Combine(modelsDir, "selection.json");
        if (File.Exists(selectionPath))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<ModelSelectionDto>(File.ReadAllText(selectionPath), JsonOpts);
                if (!string.IsNullOrWhiteSpace(dto?.ModelFileName)
                    || !string.IsNullOrWhiteSpace(dto?.ModelFullPath))
                {
                    return true;
                }
            }
            catch
            {
                /* ignore */
            }
        }

        foreach (var dir in ModelLibrarySettings.EnumerateModelFolders(modelsDir, dataDir))
        {
            if (ChatGgufSelection.ListChatGguf(dir).Count > 0)
                return true;
        }

        return false;
    }

    private static void SaveSelection(string modelsDir, string fileName, string fullPath, string root)
    {
        Directory.CreateDirectory(modelsDir);
        string? mmprojFileName = null;
        string? mmprojFullPath = null;
        var modelDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(modelDir))
        {
            var mmproj = MmprojSupport.FindMmprojForModel(fileName, modelDir, root);
            if (mmproj is not null)
            {
                mmprojFileName = mmproj.Name;
                mmprojFullPath = mmproj.FullName;
            }
        }

        var selection = new ModelSelectionDto(fileName, mmprojFileName, fullPath, mmprojFullPath);
        File.WriteAllText(
            Path.Combine(modelsDir, "selection.json"),
            JsonSerializer.Serialize(selection, JsonOpts));
    }

    private static ModelLibrarySettings.ModelLibraryDocument? TryLoadDocument(string dataDir) =>
        ModelLibrarySettings.TryLoadDocument(dataDir);
}
