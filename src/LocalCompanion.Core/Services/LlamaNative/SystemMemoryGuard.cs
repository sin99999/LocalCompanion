using LocalCompanion.Localization;

namespace LocalCompanion.Services.LlamaNative;

/// <summary>既定モデル（Gemma 4 E2B）起動に必要なメモリを、GPU/CPU 構成ごとに見積もる。</summary>
internal static class SystemMemoryGuard
{
    /// <summary>絶対下限。これ未満は起動不可。</summary>
    private const long AbsoluteMinimumRamBytes = 6L * 1024 * 1024 * 1024;

    /// <summary>Windows 常駐＋アプリ基盤の控除（実使用は環境差が大きい）。</summary>
    private const long WindowsReservedBytes = 3_600_000_000L;

    /// <summary>llama-server 本体・スレッド・一時バッファ。</summary>
    private const long LlamaRuntimeOverheadBytes = 900_000_000L;

    /// <summary>gemma-4-E2B_q4_0-it.gguf（Google）のおおよその展開サイズ。</summary>
    private const long DefaultModelBytes = 3_350_000_000L;

    /// <summary>gemma-4-E2B-it-mmproj.gguf のおおよその展開サイズ。</summary>
    private const long DefaultMmprojBytes = 987_000_000L;

    /// <summary>GPU オフロード時に CPU 側へ残る重み・入出力バッファ。</summary>
    private const long GpuCpuSideReserveBytes = 1_200_000_000L;

    internal static string InsufficientMemoryMessage =>
        LocalizationService.Instance.Get("Error.MemoryInsufficient");

    internal static void EnsureSufficientMemoryOrThrow()
    {
        EnsureCanRunModelOrThrow(null, null, contextLength: 8192, hasUsableGpu: false);
    }

    internal static void EnsureCanRunModelOrThrow(
        string? modelPath,
        string? mmprojPath,
        int contextLength,
        bool hasUsableGpu)
    {
        var hw = LlamaHardwareProfile.Current;
        if (hw.TotalPhysicalMemoryBytes <= 0)
            return;

        var modelBytes = ResolveFileBytes(modelPath, DefaultModelBytes);
        var mmprojBytes = ResolveFileBytes(mmprojPath, DefaultMmprojBytes);
        var kvBytes = EstimateKvCacheBytes(contextLength);
        var weightsBytes = modelBytes + mmprojBytes;

        if (hw.TotalPhysicalMemoryBytes < AbsoluteMinimumRamBytes)
            throw new InvalidOperationException(InsufficientMemoryMessage);

        if (TryAssess(hw, weightsBytes, kvBytes, hasUsableGpu, out var noteKey))
        {
            if (noteKey is not null)
                NativeLog.WriteKey(noteKey);
            return;
        }

        throw new InvalidOperationException(InsufficientMemoryMessage);
    }

    internal static bool TryAssess(
        LlamaHardwareSnapshot hw,
        long weightsBytes,
        long kvBytes,
        bool hasUsableGpu,
        out string? advisoryNoteKey)
    {
        advisoryNoteKey = null;
        var systemRam = hw.TotalPhysicalMemoryBytes;
        var vram = hw.DedicatedVramBytes;

        if (hw.UsesUnifiedMemory)
        {
            var required = WindowsReservedBytes + LlamaRuntimeOverheadBytes + weightsBytes + kvBytes;
            const long unifiedRecommendedBytes = 16L * 1024 * 1024 * 1024;
            const long unifiedMinimumBytes = 14L * 1024 * 1024 * 1024;

            if (systemRam >= unifiedMinimumBytes && systemRam >= required)
            {
                if (systemRam < unifiedRecommendedBytes)
                    advisoryNoteKey = "Startup.Memory.UnifiedRecommend";
                return true;
            }

            return false;
        }

        if (hasUsableGpu && vram >= 5L * 1024 * 1024 * 1024)
        {
            var gpuWeightsNeed = (long)(weightsBytes * 0.85) + kvBytes + 500_000_000L;
            const long gpuSystemMinimumBytes = 8L * 1024 * 1024 * 1024;

            if (vram >= gpuWeightsNeed && systemRam >= gpuSystemMinimumBytes)
            {
                if (vram < 6L * 1024 * 1024 * 1024 || systemRam < 10L * 1024 * 1024 * 1024)
                    advisoryNoteKey = "Startup.Memory.TightGpu";
                return true;
            }
        }

        if (hasUsableGpu && vram >= 4L * 1024 * 1024 * 1024 && systemRam >= 10L * 1024 * 1024 * 1024)
        {
            advisoryNoteKey = "Startup.Memory.LowVram";
            return true;
        }

        var cpuRequired = WindowsReservedBytes + LlamaRuntimeOverheadBytes + weightsBytes + kvBytes;
        const long cpuRecommendedBytes = 12L * 1024 * 1024 * 1024;

        if (systemRam >= cpuRecommendedBytes && systemRam >= cpuRequired)
        {
            if (systemRam < 14L * 1024 * 1024 * 1024)
                advisoryNoteKey = "Startup.Memory.CpuTight";
            return true;
        }

        return false;
    }

    private static long ResolveFileBytes(string? path, long fallback)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return fallback;

        try
        {
            var len = new FileInfo(path).Length;
            return len > 0 ? len : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>E2B クラスモデルの KV キャッシュ概算（バイト）。</summary>
    private static long EstimateKvCacheBytes(int contextLength)
    {
        var ctx = Math.Clamp(contextLength, 2048, 16384);
        return (long)ctx * 180_000L;
    }
}
