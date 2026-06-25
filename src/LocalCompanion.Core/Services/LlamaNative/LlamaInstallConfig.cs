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
        string? DataDirectory);

    internal static Settings Load(string root)
    {
        var path = Path.Combine(root, "appsettings.json");
        var ctx = 8192;
        var gpu = 99;
        var port = 8080;
        string? llamaBase = null;
        string? model = null;
        string? mmproj = null;
        string? dataDir = null;

        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("LlamaCompanion", out var lc))
                {
                    if (lc.TryGetProperty("ContextLength", out var c) && c.TryGetInt32(out var cv))
                        ctx = cv;
                    if (lc.TryGetProperty("GpuLayers", out var g) && g.TryGetInt32(out var gv))
                        gpu = gv;
                    if (lc.TryGetProperty("LlamaServerBaseUrl", out var u))
                        llamaBase = u.GetString();
                    if (lc.TryGetProperty("ModelGgufPath", out var m))
                        model = m.GetString();
                    if (lc.TryGetProperty("MmprojGgufPath", out var p))
                        mmproj = p.GetString();
                    if (lc.TryGetProperty("DataDirectory", out var d))
                        dataDir = d.GetString();
                }
            }
            catch
            {
                /* defaults */
            }
        }

        if (!string.IsNullOrWhiteSpace(llamaBase) && Uri.TryCreate(llamaBase, UriKind.Absolute, out var uri))
            port = uri.Port;

        var charCtx = TryReadCharacterContext(dataDir);
        if (charCtx > 0)
            ctx = CharacterSamplingLimits.SnapContextLength(charCtx);
        else
            ctx = CharacterSamplingLimits.SnapContextLength(ctx);

        return new Settings(ctx, gpu, port, model, mmproj, dataDir);
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
