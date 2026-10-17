using System.Text.RegularExpressions;

namespace LocalCompanion.Services.LlamaNative;

internal readonly record struct LlamaDraftCandidate(string FullPath, string FileName, long SizeBytes);

internal readonly record struct LlamaLaunchDecision(string ProfileName, IReadOnlyList<string> ExtraArgs);

/// <summary>
/// 選んだ GGUF は変えず、機械とファイル名から llama-server の追加引数だけを決める。
/// </summary>
internal static class LlamaLaunchPolicy
{
    internal const int MidCpuMoeLayers = 8;
    internal const int DraftMaxTokens = 8;
    internal const double PlentyHeadroomGb = 6;
    internal const double TightFitRatio = 0.85;
    internal const double DraftLeftoverGb = 2;
    internal const double LargeModelGb = 8;
    internal const double MediumModelGb = 3;

    internal enum VramBand
    {
        CpuOnly,
        Tight,
        Mid,
        Plenty,
    }

    internal static LlamaLaunchDecision Build(
        bool enabled,
        LlamaHardwareSnapshot hardware,
        string modelFileName,
        double modelSizeGb,
        long modelSizeBytes,
        bool hasUsableGpu,
        IReadOnlyList<LlamaDraftCandidate> draftCandidates,
        LlamaDraftCliSupport? draftCli = null)
    {
        if (!enabled)
            return new LlamaLaunchDecision("off", []);

        var cli = draftCli ?? LlamaDraftCliSupport.AssumeCurrent;
        var band = ClassifyBand(hardware, modelSizeGb, hasUsableGpu);
        var moe = LooksLikeMoe(modelFileName) && hasUsableGpu && band is VramBand.Mid or VramBand.Tight;
        var kv = ChooseKv(band, modelSizeGb);
        var draft = cli.CanAttachModel
            ? TryPickDraft(
                hardware,
                band,
                hasUsableGpu,
                modelFileName,
                modelSizeBytes,
                draftCandidates)
            : null;

        var cacheMib = moe && cli.MoeCacheMib
            ? ChooseMoeCacheMib(hardware.DedicatedVramBytes, band)
            : null;
        var args = new List<string>();
        if (moe && band == VramBand.Tight)
            args.Add("--cpu-moe");
        else if (moe)
            args.AddRange(["--n-cpu-moe", MidCpuMoeLayers.ToString()]);
        if (cacheMib is int cache)
            args.AddRange(["--moe-cache-mib", cache.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (kv is not null)
            args.AddRange(["--cache-type-k", kv, "--cache-type-v", kv]);
        if (draft is not null)
        {
            args.AddRange(["-md", draft.Value.FullPath]);
            if (cli.MaxTokensArg is string maxFlag)
                args.AddRange([maxFlag, DraftMaxTokens.ToString()]);
        }

        return new LlamaLaunchDecision(BuildProfileName(band, moe, kv, cacheMib, draft is not null), args);
    }

    internal static VramBand ClassifyBand(LlamaHardwareSnapshot hardware, double modelSizeGb, bool hasUsableGpu)
    {
        if (!hasUsableGpu)
            return VramBand.CpuOnly;

        var vramGb = hardware.DedicatedVramBytes / (1024.0 * 1024 * 1024);
        if (vramGb < modelSizeGb * TightFitRatio)
            return VramBand.Tight;
        if (vramGb < modelSizeGb + PlentyHeadroomGb)
            return VramBand.Mid;
        return VramBand.Plenty;
    }

    internal static bool LooksLikeMoe(string modelFileName)
    {
        if (string.IsNullOrWhiteSpace(modelFileName))
            return false;

        return modelFileName.Contains("MoE", StringComparison.OrdinalIgnoreCase)
            || modelFileName.Contains("mixtral", StringComparison.OrdinalIgnoreCase)
            || ActiveExpertMark.IsMatch(modelFileName);
    }

    internal static bool RemoveCpuMoeArgs(List<string> args)
    {
        var removed = false;
        var i = 0;
        while (i < args.Count)
        {
            if (string.Equals(args[i], "--cpu-moe", StringComparison.OrdinalIgnoreCase))
            {
                args.RemoveAt(i);
                removed = true;
                continue;
            }

            if ((string.Equals(args[i], "--n-cpu-moe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[i], "--moe-cache-mib", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Count)
            {
                args.RemoveRange(i, 2);
                removed = true;
                continue;
            }

            i++;
        }

        return removed;
    }

    private static readonly Regex ActiveExpertMark = new(
        @"(?:^|[^a-z0-9])a\d+b(?:[^a-z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// CPU に置いた専門家のうち、よく選ばれる分だけ GPU に残す MiB。
    /// 全部載る帯では足さない。256MiB 単位。狭いとき VRAM の 1/4 で上限 4096、中間は 1/10 で上限 2048。
    /// </summary>
    internal static int? ChooseMoeCacheMib(long dedicatedVramBytes, VramBand band)
    {
        if (band is not (VramBand.Tight or VramBand.Mid))
            return null;

        var vramMib = dedicatedVramBytes / (1024.0 * 1024);
        if (vramMib < 4096)
            return null;

        var fraction = band == VramBand.Tight ? 0.25 : 0.10;
        var cap = band == VramBand.Tight ? 4096 : 2048;
        var raw = (int)Math.Floor(vramMib * fraction);
        raw -= raw % 256;
        if (raw < 512)
            return null;
        return Math.Min(raw, cap);
    }

    internal static string? ChooseKv(VramBand band, double modelSizeGb)
    {
        switch (band)
        {
            case VramBand.Plenty:
                return null;
            case VramBand.Mid:
                return modelSizeGb >= LargeModelGb ? "q8_0" : null;
            case VramBand.Tight:
                return modelSizeGb >= 12 ? "q4_0" : "q8_0";
            case VramBand.CpuOnly:
                if (modelSizeGb >= LargeModelGb)
                    return "q4_0";
                if (modelSizeGb >= MediumModelGb)
                    return "q8_0";
                return null;
            default:
                return null;
        }
    }

    internal static LlamaDraftCandidate? TryPickDraft(
        LlamaHardwareSnapshot hardware,
        VramBand band,
        bool hasUsableGpu,
        string mainFileName,
        long mainSizeBytes,
        IReadOnlyList<LlamaDraftCandidate> candidates)
    {
        if (!hasUsableGpu || candidates.Count == 0)
            return null;
        if (band is VramBand.CpuOnly or VramBand.Tight)
            return null;
        if (!HasDraftHeadroom(hardware, mainSizeBytes))
            return null;

        var family = FamilyKey(mainFileName);
        if (string.IsNullOrEmpty(family))
            return null;

        LlamaDraftCandidate? namedDraft = null;
        LlamaDraftCandidate? smallest = null;
        foreach (var item in candidates)
        {
            if (LooksLikeMmproj(item.FileName))
                continue;
            if (item.SizeBytes <= 0 || item.SizeBytes >= mainSizeBytes)
                continue;
            if (!FamilyKey(item.FileName).Equals(family, StringComparison.Ordinal))
                continue;
            if (!DraftFitsLeftover(hardware, mainSizeBytes, item.SizeBytes))
                continue;

            if (item.FileName.Contains("draft", StringComparison.OrdinalIgnoreCase))
            {
                if (namedDraft is null || item.SizeBytes < namedDraft.Value.SizeBytes)
                    namedDraft = item;
            }

            if (smallest is null || item.SizeBytes < smallest.Value.SizeBytes)
                smallest = item;
        }

        return namedDraft ?? smallest;
    }

    internal static IReadOnlyList<LlamaDraftCandidate> ListDraftCandidates(
        string mainModelPath,
        IEnumerable<string> modelFolders)
    {
        var result = new List<LlamaDraftCandidate>();
        string mainFull;
        try
        {
            mainFull = Path.GetFullPath(mainModelPath);
        }
        catch
        {
            return result;
        }

        foreach (var folder in modelFolders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.gguf");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(file);
                }
                catch
                {
                    continue;
                }

                if (string.Equals(full, mainFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                long size;
                try
                {
                    size = new FileInfo(full).Length;
                }
                catch
                {
                    continue;
                }

                result.Add(new LlamaDraftCandidate(full, Path.GetFileName(full), size));
            }
        }

        return result;
    }

    internal static string FamilyKey(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        if (name.Contains("gemma-4", StringComparison.Ordinal))
            return "gemma-4";
        if (name.Contains("mixtral", StringComparison.Ordinal))
            return "mixtral";
        if (name.Contains("qwen2.5", StringComparison.Ordinal))
            return "qwen2.5";
        if (name.Contains("qwen2", StringComparison.Ordinal))
            return "qwen2";
        if (name.Contains("llama-3", StringComparison.Ordinal))
            return "llama-3";

        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && !IsQuantPart(parts[0]) && !IsQuantPart(parts[1]))
            return parts[0] + "-" + parts[1];
        return parts.Length > 0 ? parts[0] : name;
    }

    internal static bool LooksLikeMmproj(string fileName) =>
        fileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase);

    private static bool HasDraftHeadroom(LlamaHardwareSnapshot hardware, long mainSizeBytes)
    {
        var mainGb = mainSizeBytes / (1024.0 * 1024 * 1024);
        var vramGb = hardware.DedicatedVramBytes / (1024.0 * 1024 * 1024);
        if (vramGb - mainGb >= DraftLeftoverGb)
            return true;

        if (!hardware.UsesUnifiedMemory)
            return false;

        var ramGb = hardware.TotalPhysicalMemoryBytes / (1024.0 * 1024 * 1024);
        return ramGb - mainGb >= PlentyHeadroomGb;
    }

    /// <summary>本モデルのあとに下書き本体が載る余白があるか（閾値だけの判定ではない）。</summary>
    internal static bool DraftFitsLeftover(
        LlamaHardwareSnapshot hardware,
        long mainSizeBytes,
        long draftSizeBytes)
    {
        var mainGb = mainSizeBytes / (1024.0 * 1024 * 1024);
        var draftGb = draftSizeBytes / (1024.0 * 1024 * 1024);
        var vramGb = hardware.DedicatedVramBytes / (1024.0 * 1024 * 1024);
        if (vramGb - mainGb >= draftGb + DraftLeftoverGb)
            return true;

        if (!hardware.UsesUnifiedMemory)
            return false;

        var ramGb = hardware.TotalPhysicalMemoryBytes / (1024.0 * 1024 * 1024);
        return ramGb - mainGb >= draftGb + PlentyHeadroomGb;
    }

    private static bool IsQuantPart(string part) =>
        part.StartsWith("q", StringComparison.Ordinal)
        || part.StartsWith("iq", StringComparison.Ordinal)
        || part.StartsWith("ud", StringComparison.Ordinal)
        || part is "qat" or "k" or "xl";

    private static string BuildProfileName(VramBand band, bool moe, string? kv, int? cacheMib, bool draft)
    {
        var parts = new List<string> { BandName(band) };
        if (moe)
            parts.Add("moe");
        if (kv is "q8_0")
            parts.Add("kvq8");
        else if (kv is "q4_0")
            parts.Add("kvq4");
        if (cacheMib is int mib)
            parts.Add("mcache" + mib.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (draft)
            parts.Add("draft");
        return string.Join("-", parts);
    }

    private static string BandName(VramBand band) => band switch
    {
        VramBand.CpuOnly => "cpu",
        VramBand.Tight => "tight",
        VramBand.Mid => "mid",
        VramBand.Plenty => "plenty",
        _ => "cpu",
    };
}
