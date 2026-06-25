using System.Text.Json;

namespace LocalCompanion.Services.LlamaNative;

/// <summary>tools\llama-cpp\.installed.json から導入済み llama.cpp ビルド種別を読み取る。</summary>
public static class LlamaInstalledBackend
{
    public sealed record InstalledInfo(string Variant, string? CudaVersion, string? Tag);

    public static InstalledInfo? TryRead(string? toolsDirectory = null)
    {
        try
        {
            var dir = toolsDirectory ?? AppPaths.Current.ToolsDirectory;
            var path = Path.Combine(dir, ".installed.json");
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var variant = root.TryGetProperty("variant", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(variant))
                return null;

            var cuda = root.TryGetProperty("cudaVersion", out var c) ? c.GetString() : null;
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            return new InstalledInfo(
                variant,
                string.IsNullOrWhiteSpace(cuda) ? null : cuda,
                string.IsNullOrWhiteSpace(tag) ? null : tag);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>表示用文字列（例: "NVIDIA CUDA 12.4 (b5432)"）。未導入時は null。</summary>
    public static string? DescribeInstalled(string? toolsDirectory = null)
    {
        var info = TryRead(toolsDirectory);
        if (info is null)
            return null;

        var name = LlamaHardwareProfile.DescribeVariant(info.Variant);
        var version = info.CudaVersion is not null ? $" {info.CudaVersion}" : "";
        return info.Tag is not null ? $"{name}{version} ({info.Tag})" : $"{name}{version}";
    }
}
