namespace LocalCompanion.Services.LlamaNative;

/// <summary>VRAM 不足時に試す GPU レイヤー数（-ngl）の段階。</summary>
internal static class LlamaGpuLayerPolicy
{
    internal static IReadOnlyList<int> BuildLayerAttempts(int configuredGpuLayers, bool hasUsableGpu)
    {
        if (!hasUsableGpu || configuredGpuLayers <= 0)
            return [0];

        var attempts = new List<int>();
        void Add(int value)
        {
            var clamped = Math.Clamp(value, 0, configuredGpuLayers);
            if (!attempts.Contains(clamped))
                attempts.Add(clamped);
        }

        Add(configuredGpuLayers);
        Add(48);
        Add(24);
        Add(12);
        Add(0);
        return attempts;
    }
}
