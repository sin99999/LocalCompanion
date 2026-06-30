using System.Text.Json;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class ModelCatalogService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly LlamaOptions _opt;
    private readonly string _root;
    private readonly string _modelsDir;
    private readonly string _dataDir;
    private readonly string _selectionPath;

    public ModelCatalogService(AppPaths paths, IOptions<LlamaOptions> opt)
    {
        _opt = opt.Value;
        _root = paths.Root;
        _modelsDir = string.IsNullOrWhiteSpace(_opt.ModelsDirectory)
            ? paths.ModelsDirectory
            : Path.GetFullPath(_opt.ModelsDirectory);
        Directory.CreateDirectory(_modelsDir);
        _dataDir = AppPaths.ResolveUserDataDirectory(_opt.DataDirectory);
        _selectionPath = Path.Combine(_modelsDir, "selection.json");
    }

    public string ModelsDirectory => _modelsDir;

    /// <summary>追加モデルフォルダ（未設定なら null）。設定のみ data に保存し、フォルダには書き込まない。</summary>
    public string? GetAdditionalModelsFolder() => ModelLibrarySettings.LoadAdditionalFolder(_dataDir);

    public LocalModelsResponse SetAdditionalModelsFolder(string? folder)
    {
        ModelLibrarySettings.SaveAdditionalFolder(_dataDir, folder);
        return Scan();
    }

    public LocalModelsResponse Scan()
    {
        var chat = new List<GgufFileInfo>();
        var mmproj = new List<GgufFileInfo>();
        var seenChat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenMmproj = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 先頭の付属 models フォルダを優先。同名ファイルは最初の1つだけ採用する。
        foreach (var dir in ModelLibrarySettings.EnumerateModelFolders(_modelsDir, _dataDir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.gguf", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (IsMmprojFile(name))
                {
                    if (seenMmproj.Add(name))
                        mmproj.Add(ToInfo(name, file));
                }
                else if (seenChat.Add(name))
                {
                    chat.Add(ToInfo(name, file));
                }
            }
        }

        chat = chat.OrderBy(c => c.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        mmproj = mmproj.OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase).ToList();

        var selection = LoadSelection();
        selection = SanitizeSelection(selection, chat, mmproj, _root);
        if (selection.ModelFileName is null && chat.Count > 0)
        {
            selection = selection with { ModelFileName = chat[0].FileName, ModelFullPath = chat[0].FullPath };
            SaveSelection(selection);
        }

        var suggested = GuessMmproj(selection.ModelFileName, mmproj.Select(m => m.FileName).ToList(), _root);
        return new LocalModelsResponse(
            _modelsDir, chat, mmproj, selection, suggested, GetAdditionalModelsFolder());
    }

    public ModelSelectionDto GetSelection() =>
        SanitizeSelection(LoadSelection(), ScanLists().chat, ScanLists().mmproj, _root);

    public LocalModelsResponse Select(SelectModelRequest req)
    {
        var (chat, mmproj) = ScanLists();

        // フルパス指定があればそれで一意特定（追加フォルダに同名がある場合に対応）。
        var chosen = req.ModelFullPath is not null
            ? chat.FirstOrDefault(f => f.FullPath.Equals(req.ModelFullPath, StringComparison.OrdinalIgnoreCase))
            : null;
        chosen ??= chat.FirstOrDefault(f => f.FileName.Equals(req.ModelFileName, StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
            throw new LocalizedServiceException("Settings.Model.Error.NotFound", req.ModelFileName);

        string? mmName = null;
        string? mmFull = null;
        if (!string.IsNullOrWhiteSpace(req.MmprojFileName))
        {
            var mmInfo = mmproj.FirstOrDefault(f => f.FileName.Equals(req.MmprojFileName, StringComparison.OrdinalIgnoreCase));
            if (mmInfo is null)
                throw new LocalizedServiceException("Settings.Model.Error.MmprojNotFound", req.MmprojFileName);
            if (IsMmprojIncompatible(chosen.FileName, mmInfo.FileName, _root))
                throw new LocalizedServiceException("Settings.Model.Error.MmprojIncompatible");
            mmName = mmInfo.FileName;
            mmFull = mmInfo.FullPath;
        }
        else
        {
            var resolved = ResolveMmprojInfoForModel(chosen, mmproj);
            mmName = resolved?.FileName;
            mmFull = resolved?.FullPath;
        }

        SaveSelection(new ModelSelectionDto(chosen.FileName, mmName, chosen.FullPath, mmFull));
        return Scan();
    }

    public string? ResolveModelPath()
    {
        var s = GetSelection();
        if (!string.IsNullOrWhiteSpace(s.ModelFullPath) && File.Exists(s.ModelFullPath))
            return s.ModelFullPath;
        if (string.IsNullOrWhiteSpace(s.ModelFileName)) return FallbackPath(_opt.ModelGgufPath);
        var p = Path.Combine(_modelsDir, s.ModelFileName);
        return File.Exists(p) ? p : FallbackPath(_opt.ModelGgufPath);
    }

    public string? ResolveMmprojPath()
    {
        var s = GetSelection();
        if (!string.IsNullOrWhiteSpace(s.MmprojFullPath) && File.Exists(s.MmprojFullPath))
            return s.MmprojFullPath;
        if (!string.IsNullOrWhiteSpace(s.MmprojFileName))
        {
            var p = Path.Combine(_modelsDir, s.MmprojFileName);
            if (File.Exists(p)) return p;
        }

        var (_, mmproj) = ScanLists();
        var modelInfo = ResolveModelInfo(s);
        if (modelInfo is not null)
        {
            var resolved = ResolveMmprojInfoForModel(modelInfo, mmproj);
            if (resolved is not null && File.Exists(resolved.FullPath))
                return resolved.FullPath;
        }

        return FallbackPath(_opt.MmprojGgufPath);
    }

    private GgufFileInfo? ResolveModelInfo(ModelSelectionDto s)
    {
        var (chat, _) = ScanLists();
        if (!string.IsNullOrWhiteSpace(s.ModelFullPath))
        {
            var byPath = chat.FirstOrDefault(c => c.FullPath.Equals(s.ModelFullPath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null) return byPath;
        }
        if (!string.IsNullOrWhiteSpace(s.ModelFileName))
            return chat.FirstOrDefault(c => c.FileName.Equals(s.ModelFileName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>
    /// チャットモデルに対応する mmproj を、まずモデルと同じフォルダ、次に一覧全体から探す。
    /// 追加フォルダの mmproj は読み取りのみで、ファイルを書き加えない。
    /// </summary>
    private GgufFileInfo? ResolveMmprojInfoForModel(GgufFileInfo model, IReadOnlyList<GgufFileInfo> mmproj)
    {
        if (mmproj.Count == 0)
            return null;

        var modelDir = Path.GetDirectoryName(model.FullPath);
        if (!string.IsNullOrEmpty(modelDir))
        {
            var sameDir = mmproj
                .Where(m => string.Equals(Path.GetDirectoryName(m.FullPath), modelDir, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.FileName)
                .ToList();
            var sameDirGuess = GuessMmproj(model.FileName, sameDir, _root);
            if (sameDirGuess is not null)
            {
                var hit = mmproj.FirstOrDefault(m =>
                    m.FileName.Equals(sameDirGuess, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(m.FullPath), modelDir, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
        }

        var guess = GuessMmproj(model.FileName, mmproj.Select(m => m.FileName).ToList(), _root);
        if (guess is null) return null;
        return mmproj.FirstOrDefault(m => m.FileName.Equals(guess, StringComparison.OrdinalIgnoreCase));
    }

    private (List<GgufFileInfo> chat, List<GgufFileInfo> mmproj) ScanLists()
    {
        var r = Scan();
        return (r.ChatModels.ToList(), r.MmprojFiles.ToList());
    }

    private ModelSelectionDto LoadSelection()
    {
        if (!File.Exists(_selectionPath))
        {
            TryMigrateFromAppSettings();
        }

        if (!File.Exists(_selectionPath))
            return new ModelSelectionDto(null, null);

        try
        {
            var json = File.ReadAllText(_selectionPath);
            var dto = JsonSerializer.Deserialize<ModelSelectionDto>(json, JsonOpts);
            return dto ?? new ModelSelectionDto(null, null);
        }
        catch
        {
            return new ModelSelectionDto(null, null);
        }
    }

    private void SaveSelection(ModelSelectionDto selection)
    {
        AtomicFile.WriteAllText(_selectionPath, JsonSerializer.Serialize(selection, JsonOpts));
    }

    private static ModelSelectionDto SanitizeSelection(
        ModelSelectionDto selection,
        IReadOnlyList<GgufFileInfo> chat,
        IReadOnlyList<GgufFileInfo> mmproj,
        string root)
    {
        // モデルは可能ならフルパスで照合し、無ければファイル名、最後に先頭へフォールバック。
        GgufFileInfo? modelInfo = null;
        if (!string.IsNullOrWhiteSpace(selection.ModelFullPath))
            modelInfo = chat.FirstOrDefault(c => c.FullPath.Equals(selection.ModelFullPath, StringComparison.OrdinalIgnoreCase));
        if (modelInfo is null && !string.IsNullOrWhiteSpace(selection.ModelFileName))
            modelInfo = chat.FirstOrDefault(c => c.FileName.Equals(selection.ModelFileName, StringComparison.OrdinalIgnoreCase));
        modelInfo ??= chat.FirstOrDefault();

        string? model = modelInfo?.FileName;
        string? modelFull = modelInfo?.FullPath;

        GgufFileInfo? mmInfo = null;
        if (!string.IsNullOrWhiteSpace(selection.MmprojFullPath))
            mmInfo = mmproj.FirstOrDefault(m => m.FullPath.Equals(selection.MmprojFullPath, StringComparison.OrdinalIgnoreCase));
        if (mmInfo is null && !string.IsNullOrWhiteSpace(selection.MmprojFileName))
            mmInfo = mmproj.FirstOrDefault(m => m.FileName.Equals(selection.MmprojFileName, StringComparison.OrdinalIgnoreCase));

        string? mm = mmInfo?.FileName;
        string? mmFull = mmInfo?.FullPath;

        if (model is not null && mm is not null && IsMmprojIncompatible(model, mm, root))
        {
            mm = null;
            mmFull = null;
        }

        if (model is not null && mm is null)
        {
            var guess = GuessMmproj(model, mmproj.Select(m => m.FileName).ToList(), root);
            if (guess is not null)
            {
                var hit = mmproj.FirstOrDefault(m => m.FileName.Equals(guess, StringComparison.OrdinalIgnoreCase));
                mm = hit?.FileName;
                mmFull = hit?.FullPath;
            }
        }

        if (selection.ModelFileName != model || selection.MmprojFileName != mm
            || selection.ModelFullPath != modelFull || selection.MmprojFullPath != mmFull)
            return new ModelSelectionDto(model, mm, modelFull, mmFull);

        return selection;
    }

    internal static bool IsMmprojFile(string fileName) => LlamaDefaultModel.IsMmprojFileName(fileName);

    internal static string? GuessMmproj(string? modelFileName, IReadOnlyList<string> mmprojFiles, string? root = null)
    {
        if (mmprojFiles.Count == 0 || string.IsNullOrWhiteSpace(modelFileName))
            return null;

        if (!string.IsNullOrWhiteSpace(root))
        {
            var modelsDir = Path.Combine(root, "models");
            var found = MmprojFamilyRegistry.FindLocalMmproj(modelFileName, modelsDir, root);
            if (found is not null && mmprojFiles.Contains(found.Name, StringComparer.OrdinalIgnoreCase))
                return found.Name;
        }

        var stem = MmprojFamilyRegistry.NormalizeModelStem(modelFileName);
        var exact = mmprojFiles.FirstOrDefault(m =>
            m.Equals($"mmproj-{stem}-f16.gguf", StringComparison.OrdinalIgnoreCase) ||
            m.Equals($"mmproj-{stem}.gguf", StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var partial = mmprojFiles.FirstOrDefault(m =>
            m.Contains(stem, StringComparison.OrdinalIgnoreCase));
        if (partial is not null) return partial;

        var modelKey = ExtractSizeToken(modelFileName, root);
        if (string.Equals(modelKey, "E2B", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var preferred in new[] { LlamaDefaultModel.MmprojFileName, "mmproj-F16.gguf", "mmproj-BF16.gguf" })
            {
                var hit = mmprojFiles.FirstOrDefault(m =>
                    m.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }

            var generic = mmprojFiles.FirstOrDefault(m =>
                m.StartsWith("mmproj-F16", StringComparison.OrdinalIgnoreCase) ||
                m.StartsWith("mmproj-BF16", StringComparison.OrdinalIgnoreCase));
            if (generic is not null && ExtractSizeToken(generic, root) is null)
                return generic;
        }

        if (modelKey is not null)
        {
            var bySize = mmprojFiles.FirstOrDefault(m =>
                string.Equals(ExtractSizeToken(m, root), modelKey, StringComparison.OrdinalIgnoreCase));
            if (bySize is not null) return bySize;
        }

        return null;
    }

    internal static bool IsMmprojIncompatible(string? modelFileName, string? mmprojFileName, string? root = null)
    {
        if (string.IsNullOrWhiteSpace(modelFileName) || string.IsNullOrWhiteSpace(mmprojFileName))
            return false;
        var modelKey = ExtractSizeToken(modelFileName, root);
        var mmKey = ExtractSizeToken(mmprojFileName, root);
        return modelKey is not null && mmKey is not null &&
               !string.Equals(modelKey, mmKey, StringComparison.OrdinalIgnoreCase);
    }

    private static GgufFileInfo ToInfo(string fileName, string fullPath)
    {
        var exists = File.Exists(fullPath);
        var sizeGb = exists ? new FileInfo(fullPath).Length / (1024.0 * 1024 * 1024) : 0;
        return new GgufFileInfo(fileName, fullPath, Math.Round(sizeGb, 2), exists);
    }

    private void TryMigrateFromAppSettings()
    {
        if (string.IsNullOrWhiteSpace(_opt.ModelGgufPath)) return;
        var name = Path.GetFileName(_opt.ModelGgufPath);
        var path = Path.Combine(_modelsDir, name);
        if (!File.Exists(path)) return;

        string? mm = null;
        if (!string.IsNullOrWhiteSpace(_opt.MmprojGgufPath))
        {
            var mmName = Path.GetFileName(_opt.MmprojGgufPath);
            if (File.Exists(Path.Combine(_modelsDir, mmName)))
                mm = mmName;
        }

        SaveSelection(new ModelSelectionDto(name, mm));
    }

    private static string? FallbackPath(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        return File.Exists(configured) ? configured : null;
    }

    public async Task<ModelRuntimeStatus> GetRuntimeStatusAsync(LlamaServerClient llama, CancellationToken ct = default)
    {
        var selection = GetSelection();
        var selected = selection.ModelFileName;
        string? loaded = null;
        if (await llama.PingAsync(ct))
        {
            var ids = await llama.ListModelsAsync(ct);
            var id = NormalizeModelFileName(ids.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, "local", StringComparison.OrdinalIgnoreCase))
                loaded = id;
            // v1/models が空・id=local でも応答している場合は選択中モデルを起動中とみなす
            else if (!string.IsNullOrWhiteSpace(selected))
                loaded = selected;
        }

        var mismatch = selected is not null && loaded is not null &&
                       !string.Equals(selected, loaded, StringComparison.OrdinalIgnoreCase);
        var mmWarn = BuildMmprojWarning(selection.ModelFileName, selection.MmprojFileName, _root);
        return new ModelRuntimeStatus(selected, loaded, mismatch, mmWarn);
    }

    public static string? NormalizeModelFileName(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        var id = modelId.Trim();
        if (id.Contains('\\') || id.Contains('/'))
            return Path.GetFileName(id);
        return id;
    }

    public static string? BuildMmprojWarning(string? modelFileName, string? mmprojFileName, string? root = null)
    {
        if (string.IsNullOrWhiteSpace(modelFileName) || string.IsNullOrWhiteSpace(mmprojFileName))
            return null;

        var modelKey = ExtractSizeToken(modelFileName, root);
        var mmKey = ExtractSizeToken(mmprojFileName, root);
        if (modelKey is null || mmKey is null || string.Equals(modelKey, mmKey, StringComparison.OrdinalIgnoreCase))
            return null;

        return modelKey switch
        {
            "E2B" => LocalizationService.Instance.Format("Health.MmprojMismatch.E2B", mmKey),
            "26B" => LocalizationService.Instance.Format("Health.MmprojMismatch.26B", mmKey),
            _ => LocalizationService.Instance.Format("Health.MmprojMismatch.Generic", modelKey, mmKey),
        };
    }

    internal static string? ExtractSizeToken(string fileName, string? root = null)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            var fromRegistry = MmprojFamilyRegistry.GetSizeToken(fileName, root);
            if (fromRegistry is not null)
                return fromRegistry;
        }

        return ExtractSizeTokenLegacy(fileName);
    }

    internal static string? ExtractSizeTokenLegacy(string fileName)
    {
        var upper = fileName.ToUpperInvariant();
        if (upper.Contains("26B")) return "26B";
        if (upper.Contains("12B")) return "12B";
        if (upper.Contains("E2B")) return "E2B";
        if (upper.Contains("E4B")) return "E4B";
        return null;
    }
}
