using System.Text.Json;

namespace LocalCompanion.Services.LlamaNative;

internal static class MmprojBootstrapStore
{
    private const string MarkerFileName = ".mmproj-bootstrap.json";

    internal sealed record MarkerEntry(
        string Status,
        string? MmprojFileName,
        int RegistryVersion,
        string? Detail);

    internal static MarkerEntry? Read(string root, string modelFileName)
    {
        var map = ReadMap(root);
        if (!map.TryGetValue(modelFileName, out var raw) || raw is null)
            return null;

        var status = raw.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
        var mmproj = raw.TryGetValue("mmprojFileName", out var m) ? m?.ToString() : null;
        var version = raw.TryGetValue("registryVersion", out var v) && v is JsonElement je && je.TryGetInt32(out var iv)
            ? iv
            : 0;
        var detail = raw.TryGetValue("detail", out var d) ? d?.ToString() : null;
        return new MarkerEntry(status, mmproj, version, detail);
    }

    internal static bool ShouldSkipDownload(string root, string modelFileName, int currentRegistryVersion)
    {
        var entry = Read(root, modelFileName);
        if (entry is null)
            return false;

        if (string.Equals(entry.Status, "not_found", StringComparison.OrdinalIgnoreCase)
            && entry.RegistryVersion >= currentRegistryVersion)
            return true;

        if (entry.Status is "downloaded" or "present")
        {
            if (string.IsNullOrWhiteSpace(entry.MmprojFileName))
                return false;

            var path = Path.Combine(root, "models", entry.MmprojFileName);
            return File.Exists(path);
        }

        return false;
    }

    internal static void WritePresent(string root, string modelFileName, string mmprojFileName, int registryVersion) =>
        Write(root, modelFileName, new Dictionary<string, object?>
        {
            ["status"] = "present",
            ["mmprojFileName"] = mmprojFileName,
            ["registryVersion"] = registryVersion,
            ["at"] = DateTime.UtcNow.ToString("o"),
        });

    internal static void WriteDownloaded(
        string root,
        string modelFileName,
        string mmprojFileName,
        string url,
        string? repoId,
        int registryVersion) =>
        Write(root, modelFileName, new Dictionary<string, object?>
        {
            ["status"] = "downloaded",
            ["mmprojFileName"] = mmprojFileName,
            ["url"] = url,
            ["repoId"] = repoId,
            ["registryVersion"] = registryVersion,
            ["at"] = DateTime.UtcNow.ToString("o"),
        });

    internal static void WriteNotFound(string root, string modelFileName, string detail, int registryVersion) =>
        Write(root, modelFileName, new Dictionary<string, object?>
        {
            ["status"] = "not_found",
            ["detail"] = detail,
            ["registryVersion"] = registryVersion,
            ["at"] = DateTime.UtcNow.ToString("o"),
        });

    private static Dictionary<string, Dictionary<string, object?>> ReadMap(string root)
    {
        var path = Path.Combine(root, "models", MarkerFileName);
        if (!File.Exists(path))
            return new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (doc is null)
                return new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

            var map = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in doc)
            {
                if (value.ValueKind != JsonValueKind.Object)
                    continue;
                var inner = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in value.EnumerateObject())
                    inner[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt32(out var n) ? n : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString(),
                    };
                map[key] = inner;
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(string root, string modelFileName, Dictionary<string, object?> entry)
    {
        var modelsDir = Path.Combine(root, "models");
        Directory.CreateDirectory(modelsDir);
        var path = Path.Combine(modelsDir, MarkerFileName);
        var map = ReadMap(root);
        map[modelFileName] = entry;
        File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }
}
