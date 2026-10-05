namespace LocalCompanion.Services;

internal static class JuliaDevicePreferences
{
    public const string Auto = "auto";
    public const string Gpu = "gpu";
    public const string Cpu = "cpu";

    public static string Normalize(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Equals(Gpu, StringComparison.OrdinalIgnoreCase))
            return Gpu;
        if (text.Equals(Cpu, StringComparison.OrdinalIgnoreCase))
            return Cpu;
        return Auto;
    }
}

internal enum JuliaModelAction
{
    Keep,
    Remember,
    Download,
}

internal readonly record struct JuliaPlacementDecision(bool UseGpu, string Reason);

/// <summary>
/// Julia-1 を CPU に置くか、グラフィックカードに置くか。
/// 自動は、重さの全部が空きメモリに入るときだけカードにする。
/// </summary>
internal static class JuliaPlacement
{
    public const int GpuLayers = 99;
    public const long HeadroomBytes = 256L * 1024 * 1024;

    public static JuliaPlacementDecision Decide(
        string? preference,
        long modelBytes,
        long? freeVramBytes,
        bool gpuServerAvailable)
    {
        var pref = JuliaDevicePreferences.Normalize(preference);
        if (pref == JuliaDevicePreferences.Cpu)
            return new(false, "cpu");
        if (!gpuServerAvailable)
            return new(false, "no-gpu-server");
        if (pref == JuliaDevicePreferences.Gpu)
            return new(true, "gpu");
        if (modelBytes <= 0 || freeVramBytes is null)
            return new(false, "free-vram-unknown");
        if (freeVramBytes.Value >= modelBytes + HeadroomBytes)
            return new(true, "fits");
        return new(false, "vram-short");
    }

    public static JuliaModelAction DecideUpdate(
        bool autoUpdate,
        bool fileReady,
        string? savedToken,
        string? remoteToken)
    {
        if (!fileReady)
            return JuliaModelAction.Download;
        if (!autoUpdate)
            return JuliaModelAction.Keep;
        if (string.IsNullOrWhiteSpace(remoteToken))
            return JuliaModelAction.Keep;
        if (string.IsNullOrWhiteSpace(savedToken))
            return JuliaModelAction.Remember;
        if (!string.Equals(savedToken.Trim(), remoteToken.Trim(), StringComparison.Ordinal))
            return JuliaModelAction.Download;
        return JuliaModelAction.Keep;
    }
}
