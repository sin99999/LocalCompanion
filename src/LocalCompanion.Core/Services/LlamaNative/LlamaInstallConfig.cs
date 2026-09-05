using System.Text.Json;
using LocalCompanion.Models;

namespace LocalCompanion.Services.LlamaNative;

internal static class LlamaInstallConfig
{
    internal sealed record Settings(
        int ContextLength,
        int GpuLayers,
        int Port,
        string? ModelGgufPath,
        string? MmprojGgufPath,
        string? DataDirectory,
        bool AutoLaunchTune);

    internal static Settings Load(string root)
    {
        var ctx = 8192;
        var gpu = 99;
        var port = 8080;
        var autoLaunchTune = true;
        string? llamaBase = null;
        string? model = null;
        string? mmproj = null;
        string? dataDir = null;

        // Root（リポ／配布）→ ContentRoot（bin 出力）→ local の順で上書き（DI と同じ優先）
        ApplyFile(Path.Combine(root, "appsettings.json"), ref ctx, ref gpu, ref llamaBase, ref model, ref mmproj, ref dataDir, ref autoLaunchTune);
        try
        {
            var contentRoot = AppPaths.Current.ContentRoot;
            if (!string.Equals(Path.GetFullPath(contentRoot), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            {
                ApplyFile(Path.Combine(contentRoot, "appsettings.json"), ref ctx, ref gpu, ref llamaBase, ref model, ref mmproj, ref dataDir, ref autoLaunchTune);
                ApplyFile(Path.Combine(contentRoot, "appsettings.local.json"), ref ctx, ref gpu, ref llamaBase, ref model, ref mmproj, ref dataDir, ref autoLaunchTune);
            }
            else
            {
                ApplyFile(Path.Combine(root, "appsettings.local.json"), ref ctx, ref gpu, ref llamaBase, ref model, ref mmproj, ref dataDir, ref autoLaunchTune);
            }
        }
        catch
        {
            ApplyFile(Path.Combine(root, "appsettings.local.json"), ref ctx, ref gpu, ref llamaBase, ref model, ref mmproj, ref dataDir, ref autoLaunchTune);
        }

        if (!string.IsNullOrWhiteSpace(llamaBase) && Uri.TryCreate(llamaBase, UriKind.Absolute, out var uri))
            port = uri.Port;

        var charCtx = TryReadCharacterContext(dataDir);
        if (charCtx > 0)
            ctx = CharacterSamplingLimits.SnapContextLength(charCtx);
        else
            ctx = CharacterSamplingLimits.SnapContextLength(ctx);

        return new Settings(ctx, gpu, port, model, mmproj, dataDir, autoLaunchTune);
    }

    private static void ApplyFile(
        string path,
        ref int ctx,
        ref int gpu,
        ref string? llamaBase,
        ref string? model,
        ref string? mmproj,
        ref string? dataDir,
        ref bool autoLaunchTune)
    {
        if (!File.Exists(path))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("LlamaCompanion", out var lc))
                return;

            if (lc.TryGetProperty("ContextLength", out var c) && c.TryGetInt32(out var cv))
                ctx = cv;
            if (lc.TryGetProperty("GpuLayers", out var g) && g.TryGetInt32(out var gv))
                gpu = gv;
            if (lc.TryGetProperty("LlamaServerBaseUrl", out var u) && u.ValueKind == JsonValueKind.String)
                llamaBase = u.GetString();
            if (lc.TryGetProperty("ModelGgufPath", out var m) && m.ValueKind == JsonValueKind.String)
                model = m.GetString();
            if (lc.TryGetProperty("MmprojGgufPath", out var p) && p.ValueKind == JsonValueKind.String)
                mmproj = p.GetString();
            if (lc.TryGetProperty("DataDirectory", out var d) && d.ValueKind == JsonValueKind.String)
                dataDir = d.GetString();
            if (lc.TryGetProperty("AutoLaunchTune", out var tune))
            {
                if (tune.ValueKind == JsonValueKind.True)
                    autoLaunchTune = true;
                else if (tune.ValueKind == JsonValueKind.False)
                    autoLaunchTune = false;
            }
        }
        catch
        {
            /* defaults / previous overlay を維持 */
        }
    }

    private static int TryReadCharacterContext(string? dataDirectory)
    {
        var dir = AppPaths.ResolveUserDataDirectory(dataDirectory);
        var path = Path.Combine(dir, "character-settings.json");
        if (!File.Exists(path))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("contextLength", out var c) && c.TryGetInt32(out var v))
                return v;
        }
        catch
        {
            /* ignore */
        }

        return 0;
    }
}
