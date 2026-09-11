namespace LocalCompanion.Models;

/// <summary>NVIDIA 以外の GPU 向け。NVIDIA は常に CUDA。</summary>
public static class LlamaGpuBackendPreferences
{
    public const string Auto = "auto";
    public const string HipRadeon = "hip-radeon";
    public const string Vulkan = "vulkan";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, HipRadeon, StringComparison.OrdinalIgnoreCase))
            return HipRadeon;
        if (string.Equals(value, Vulkan, StringComparison.OrdinalIgnoreCase))
            return Vulkan;
        return Auto;
    }
}
