using System.Text.Json;
using LocalCompanion.Localization;

namespace LocalCompanion.Services.LlamaNative;

internal static class MmprojSupport
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(2) };

    internal sealed record ResolvedPaths(string? ModelPath, string? MmprojPath);

    internal static FileInfo? FindMmprojForModel(string modelFileName, string modelsDir, string? root = null) =>
        MmprojFamilyRegistry.FindLocalMmproj(
            modelFileName,
            modelsDir,
            root ?? InferRootFromModelsDir(modelsDir));

    internal static FileInfo? TryFindMmprojForModel(string root, string modelFileName, string? modelFullPath = null)
    {
        var modelsDir = Path.Combine(root, "models");

        var local = FindMmprojForModel(modelFileName, modelsDir, root);
        if (local is not null)
            return local;

        if (string.IsNullOrWhiteSpace(modelFullPath))
            modelFullPath = TryReadSelectionModelFullPath(modelsDir);

        if (!string.IsNullOrWhiteSpace(modelFullPath) && File.Exists(modelFullPath))
        {
            var near = FindMmprojNearModel(modelFullPath, modelsDir, root);
            if (near is not null)
                return near;
        }

        var settings = LlamaInstallConfig.Load(root);
        var dataDir = AppPaths.ResolveUserDataDirectory(settings.DataDirectory);
        var extra = ModelLibrarySettings.LoadAdditionalFolder(dataDir);
        if (!string.IsNullOrWhiteSpace(extra) && Directory.Exists(extra))
        {
            var inExtra = FindMmprojForModel(modelFileName, extra, root);
            if (inExtra is not null)
                return inExtra;
        }

        return null;
    }

    internal static bool NeedsKnownMmprojDownload(string root, string modelFileName, string? modelFullPath = null)
    {
        if (string.IsNullOrWhiteSpace(modelFileName)
            || modelFileName.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!MmprojFamilyRegistry.LooksVisionCapable(modelFileName, root))
            return false;

        if (TryFindMmprojForModel(root, modelFileName, modelFullPath) is not null)
            return false;

        var modelsDir = Path.Combine(root, "models");
        var registryVersion = MmprojFamilyRegistry.GetRegistryVersion(root);
        if (MmprojBootstrapStore.ShouldSkipDownload(root, modelFileName, registryVersion))
            return false;

        var matched = MmprojFamilyRegistry.TryMatchFamily(modelFileName, root);
        if (matched is not null)
        {
            var canonical = MmprojFamilyRegistry.GetCanonicalSpec(matched);
            if (canonical is not null)
            {
                var dest = Path.Combine(modelsDir, canonical.LocalName);
                if (File.Exists(dest) && new FileInfo(dest).Length > 10_000_000)
                    return false;
            }

            return true;
        }

        return true;
    }

    internal static void TryEnsureKnownMmproj(string root, string modelFileName, string? modelFullPath = null) =>
        EnsureKnownMmprojAsync(root, modelFileName, modelFullPath).GetAwaiter().GetResult();

    internal static async Task EnsureKnownMmprojAsync(
        string root,
        string modelFileName,
        string? modelFullPath = null,
        CancellationToken ct = default)
    {
        if (!NeedsKnownMmprojDownload(root, modelFileName, modelFullPath))
            return;

        var modelsDir = Path.Combine(root, "models");
        var registryVersion = MmprojFamilyRegistry.GetRegistryVersion(root);
        var matched = MmprojFamilyRegistry.TryMatchFamily(modelFileName, root);

        var spec = matched is not null ? MmprojFamilyRegistry.GetCanonicalSpec(matched) : null;
        spec ??= await MmprojHuggingFaceDiscovery.TryDiscoverAsync(root, modelFileName, matched, ct);

        if (spec is null)
        {
            MmprojBootstrapStore.WriteNotFound(
                root,
                modelFileName,
                "No compatible mmproj found on Hugging Face (text-only).",
                registryVersion);
            return;
        }

        var dest = Path.Combine(modelsDir, spec.LocalName);
        NativeLog.WriteKey("Startup.Mmproj.Download", null, spec.LocalName);
        try
        {
            Directory.CreateDirectory(modelsDir);
            using var response = await Http.GetAsync(spec.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var fs = File.Create(dest);
            await Task.Run(
                () => DownloadProgress.CopyStream(stream, fs, "Startup.Download.Label.Mmproj", total, spec.LocalName),
                ct);

            if (!File.Exists(dest) || new FileInfo(dest).Length < 10_000_000)
                throw new InvalidOperationException("Downloaded mmproj file is too small.");

            MmprojBootstrapStore.WriteDownloaded(
                root,
                modelFileName,
                spec.LocalName,
                spec.Url,
                spec.RepoId,
                registryVersion);
            NativeLog.WriteKey("Startup.Mmproj.Done", null, spec.LocalName);
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
            throw;
        }
        catch (Exception ex)
        {
            NativeLog.WriteKey("Startup.Mmproj.DownloadFailed", null, UserFacingErrorLocalizer.Localize(ex));
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
        }
    }

    internal static ResolvedPaths ResolveModelPaths(
        string root,
        string modelsDir,
        string? modelOverride,
        string? mmprojOverride,
        string? configModel,
        string? configMmproj,
        IReadOnlyList<string>? extraFolders = null)
    {
        Directory.CreateDirectory(modelsDir);
        var selectionPath = Path.Combine(modelsDir, "selection.json");

        string? model = ResolveGgufPath(root, modelOverride ?? configModel);
        string? mmproj = ResolveGgufPath(root, mmprojOverride ?? configMmproj);

        if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(mmproj))
        {
            var sel = ReadSelection(selectionPath, modelsDir, root);
            model ??= sel.Model;
            mmproj ??= sel.Mmproj;
        }

        if (string.IsNullOrEmpty(model) || !File.Exists(model))
        {
            var chat = EnumerateChatGgufs(modelsDir, extraFolders);
            if (chat.Count == 1)
            {
                model = chat[0];
                var baseName = Path.GetFileName(model);
                if (string.IsNullOrEmpty(mmproj) || !File.Exists(mmproj))
                {
                    var found = FindMmprojNearModel(model, modelsDir, root);
                    if (found is not null)
                        mmproj = found.FullName;
                }
                TryWriteSelection(selectionPath, baseName, model, mmproj);
                NativeLog.WriteKey("Startup.ModelAutoSelect", null, baseName);
            }
        }

        if (!string.IsNullOrEmpty(model) && File.Exists(model))
        {
            var modelBase = Path.GetFileName(model);
            if (string.IsNullOrEmpty(mmproj) || !File.Exists(mmproj))
            {
                var found = FindMmprojNearModel(model, modelsDir, root);
                if (found is not null)
                    mmproj = found.FullName;
            }
            FixMmprojMismatches(modelsDir, selectionPath, ref model, ref mmproj, root);
        }

        return new ResolvedPaths(
            string.IsNullOrEmpty(model) || !File.Exists(model) ? null : model,
            string.IsNullOrEmpty(mmproj) || !File.Exists(mmproj) ? null : mmproj);
    }

    private static List<string> EnumerateChatGgufs(string modelsDir, IReadOnlyList<string>? extraFolders)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new List<string> { modelsDir };
        if (extraFolders is not null)
            dirs.AddRange(extraFolders);

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.gguf"))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(name))
                    result.Add(f);
            }
        }

        return result;
    }

    /// <summary>モデルと同じフォルダ → 付属 models の順で mmproj を探す（追加フォルダは読み取りのみ）。</summary>
    private static FileInfo? FindMmprojNearModel(string modelFullPath, string modelsDir, string root)
    {
        var modelBase = Path.GetFileName(modelFullPath);
        var modelDir = Path.GetDirectoryName(modelFullPath);
        if (!string.IsNullOrEmpty(modelDir)
            && !string.Equals(modelDir, modelsDir, StringComparison.OrdinalIgnoreCase))
        {
            var sameDir = FindMmprojForModel(modelBase, modelDir, root);
            if (sameDir is not null)
                return sameDir;
        }

        return FindMmprojForModel(modelBase, modelsDir, root);
    }

    private static void FixMmprojMismatches(
        string modelsDir,
        string selectionPath,
        ref string model,
        ref string? mmproj,
        string root)
    {
        var modelBase = Path.GetFileName(model);
        var mmprojBase = mmproj is not null ? Path.GetFileName(mmproj) : "";
        var hasMmproj = !string.IsNullOrEmpty(mmproj) && File.Exists(mmproj);
        var modelKey = MmprojFamilyRegistry.GetSizeToken(modelBase, root)
            ?? ModelCatalogService.ExtractSizeTokenLegacy(modelBase);
        var mmKey = hasMmproj
            ? MmprojFamilyRegistry.GetSizeToken(mmprojBase, root) ?? ModelCatalogService.ExtractSizeTokenLegacy(mmprojBase)
            : null;

        if (modelKey is not null && mmKey is not null
            && !string.Equals(modelKey, mmKey, StringComparison.OrdinalIgnoreCase))
        {
            var fix = FindMmprojForModel(modelBase, modelsDir, root);
            if (fix is null && string.Equals(modelKey, "26B", StringComparison.OrdinalIgnoreCase))
                throw new LocalizedServiceException("Settings.Model.Error.MmprojRequired26B");
            if (fix is null && string.Equals(modelKey, "E2B", StringComparison.OrdinalIgnoreCase))
                throw new LocalizedServiceException("Settings.Model.Error.MmprojRequiredE2B");
            if (fix is not null)
            {
                mmproj = fix.FullName;
                TryWriteSelection(selectionPath, modelBase, model, mmproj);
            }
        }
        else if (modelBase.Contains("E2B", StringComparison.OrdinalIgnoreCase) && hasMmproj)
        {
            var bad = mmprojBase.Contains("E4B", StringComparison.OrdinalIgnoreCase)
                || mmprojBase.Contains("12B", StringComparison.OrdinalIgnoreCase)
                || mmprojBase.Contains("Uncensored", StringComparison.OrdinalIgnoreCase)
                || mmprojBase.Contains("Gemma-4-E4B", StringComparison.OrdinalIgnoreCase)
                || !LlamaDefaultModel.IsE2BMmprojFileName(mmprojBase);
            if (bad)
            {
                var fix = FindMmprojForModel(modelBase, modelsDir, root);
                if (fix is null)
                    throw new LocalizedServiceException("Settings.Model.Error.MmprojRequiredE2B");
                mmproj = fix.FullName;
                TryWriteSelection(selectionPath, modelBase, model, mmproj);
            }
        }
    }

    private static string? TryReadSelectionModelFullPath(string modelsDir)
    {
        var selectionPath = Path.Combine(modelsDir, "selection.json");
        if (!File.Exists(selectionPath))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(selectionPath));
            var rootEl = doc.RootElement;
            if (rootEl.TryGetProperty("ModelFullPath", out var full) && full.ValueKind == JsonValueKind.String)
                return full.GetString();
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static string InferRootFromModelsDir(string modelsDir) =>
        Directory.GetParent(modelsDir)?.FullName ?? modelsDir;

    private static string? ResolveGgufPath(string root, string? p)
    {
        if (string.IsNullOrWhiteSpace(p))
            return null;
        if (Path.IsPathRooted(p))
            return File.Exists(p) ? p : null;
        var fromRoot = Path.Combine(root, p);
        return File.Exists(fromRoot) ? fromRoot : null;
    }

    private static (string? Model, string? Mmproj) ReadSelection(string selectionPath, string modelsDir, string root)
    {
        if (!File.Exists(selectionPath))
            return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(selectionPath));
            var rootEl = doc.RootElement;
            var modelName = rootEl.TryGetProperty("ModelFileName", out var m) ? m.GetString()
                : rootEl.TryGetProperty("model", out var m2) ? m2.GetString() : null;
            var mmprojName = rootEl.TryGetProperty("MmprojFileName", out var p) ? p.GetString()
                : rootEl.TryGetProperty("mmproj", out var p2) ? p2.GetString() : null;
            var modelFull = rootEl.TryGetProperty("ModelFullPath", out var mf) ? mf.GetString() : null;
            var mmprojFull = rootEl.TryGetProperty("MmprojFullPath", out var pf) ? pf.GetString() : null;

            string? model = null;
            string? mmproj = null;
            // フルパス（追加フォルダ含む）を優先し、無ければ付属 models 内のファイル名で解決。
            if (!string.IsNullOrEmpty(modelFull) && File.Exists(modelFull))
                model = modelFull;
            else if (!string.IsNullOrEmpty(modelName))
            {
                var mp = Path.Combine(modelsDir, modelName);
                if (File.Exists(mp))
                    model = mp;
            }
            if (!string.IsNullOrEmpty(mmprojFull) && File.Exists(mmprojFull))
                mmproj = mmprojFull;
            else if (!string.IsNullOrEmpty(mmprojName))
            {
                var pp = Path.Combine(modelsDir, mmprojName);
                if (File.Exists(pp))
                    mmproj = pp;
            }

            if (model is not null && mmproj is not null)
            {
                var mb = Path.GetFileName(model);
                var pb = Path.GetFileName(mmproj);
                if (ModelCatalogService.IsMmprojIncompatible(mb, pb, root))
                    mmproj = null;
            }

            if (model is not null && mmproj is null)
            {
                var found = FindMmprojNearModel(model, modelsDir, root);
                if (found is not null)
                    mmproj = found.FullName;
            }

            return (model, mmproj);
        }
        catch
        {
            return (null, null);
        }
    }

    private static void TryWriteSelection(string selectionPath, string modelFileName, string? modelFullPath, string? mmprojPath)
    {
        try
        {
            var obj = new Dictionary<string, string?> { ["ModelFileName"] = modelFileName };
            if (!string.IsNullOrEmpty(modelFullPath))
                obj["ModelFullPath"] = modelFullPath;
            if (!string.IsNullOrEmpty(mmprojPath))
            {
                obj["MmprojFileName"] = Path.GetFileName(mmprojPath);
                obj["MmprojFullPath"] = mmprojPath;
            }
            File.WriteAllText(selectionPath, System.Text.Json.JsonSerializer.Serialize(obj));
        }
        catch
        {
            /* ignore */
        }
    }
}
