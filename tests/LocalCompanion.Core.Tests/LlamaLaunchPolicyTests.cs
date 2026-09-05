using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaLaunchPolicyTests
{
    private const string Moe26B = "gemma-4-26B-A4B-it-qat-UD-Q4_K_XL.gguf";
    private const string DenseE2B = "gemma-4-E2B-it-qat-Q4_0.gguf";

    [Fact]
    public void Build_Disabled_ReturnsOffAndNoArgs()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: false,
            Gpu(vramGb: 24),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            []);

        Assert.Equal("off", decision.ProfileName);
        Assert.Empty(decision.ExtraArgs);

        var existing = new List<string> { "-np", "1" };
        existing.AddRange(decision.ExtraArgs);
        Assert.Equal(["-np", "1"], existing);
    }

    [Fact]
    public void Build_PlentyVramMoe_NoExtraArgs()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 24),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            []);

        Assert.Equal("plenty", decision.ProfileName);
        Assert.Empty(decision.ExtraArgs);
    }

    [Fact]
    public void Build_MidVramMoe_AddsCpuExpertsAndKvQ8()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 16),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            []);

        Assert.Equal("mid-moe-kvq8", decision.ProfileName);
        Assert.Equal(
            ["--n-cpu-moe", "8", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0"],
            decision.ExtraArgs);
    }

    [Fact]
    public void Build_MidVramDense_NoMoeFlag()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 8),
            DenseE2B,
            modelSizeGb: 3.4,
            modelSizeBytes: Gb(3.4),
            hasUsableGpu: true,
            []);

        Assert.Equal("mid", decision.ProfileName);
        Assert.DoesNotContain("--n-cpu-moe", decision.ExtraArgs);
    }

    [Fact]
    public void Build_TightLargeMoe_UsesKvQ4WithoutMoeFlag()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 8),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            []);

        Assert.Equal("tight-kvq4", decision.ProfileName);
        Assert.DoesNotContain("--n-cpu-moe", decision.ExtraArgs);
        Assert.Equal(
            ["--cache-type-k", "q4_0", "--cache-type-v", "q4_0"],
            decision.ExtraArgs);
    }

    [Fact]
    public void Build_CpuOnlyLarge_UsesKvQ4()
    {
        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Cpu(ramGb: 32),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: false,
            []);

        Assert.Equal("cpu-kvq4", decision.ProfileName);
        Assert.Equal(
            ["--cache-type-k", "q4_0", "--cache-type-v", "q4_0"],
            decision.ExtraArgs);
    }

    [Fact]
    public void Build_PlentyWithDraftCandidate_AddsDraftArgs()
    {
        var draft = new LlamaDraftCandidate(
            @"D:\models\gemma-4-E2B-it-draft.gguf",
            "gemma-4-E2B-it-draft.gguf",
            Gb(3));

        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 24),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            [draft]);

        Assert.Equal("plenty-draft", decision.ProfileName);
        Assert.Equal(
            ["-md", draft.FullPath, "--spec-draft-n-max", "8"],
            decision.ExtraArgs);
    }

    [Fact]
    public void Build_PlentyWithDraft_SkipsWhenCliCannotAttach()
    {
        var draft = new LlamaDraftCandidate(
            @"D:\models\gemma-4-E2B-it-draft.gguf",
            "gemma-4-E2B-it-draft.gguf",
            Gb(3));

        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 24),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            [draft],
            LlamaDraftCliSupport.None);

        Assert.Equal("plenty", decision.ProfileName);
        Assert.DoesNotContain("-md", decision.ExtraArgs);
    }

    [Fact]
    public void Build_PlentyWithDraft_LegacyMaxFlag()
    {
        var draft = new LlamaDraftCandidate(
            @"D:\models\gemma-4-E2B-it-draft.gguf",
            "gemma-4-E2B-it-draft.gguf",
            Gb(3));

        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 24),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            [draft],
            new LlamaDraftCliSupport(true, LlamaDraftMaxFlag.LegacyDraftMax));

        Assert.Equal(
            ["-md", draft.FullPath, "--draft-max", "8"],
            decision.ExtraArgs);
    }

    [Fact]
    public void Build_TightIgnoresDraftEvenIfPresent()
    {
        var draft = new LlamaDraftCandidate(
            @"D:\models\gemma-4-E2B-it-draft.gguf",
            "gemma-4-E2B-it-draft.gguf",
            Gb(3));

        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 8),
            Moe26B,
            modelSizeGb: 15,
            modelSizeBytes: Gb(15),
            hasUsableGpu: true,
            [draft]);

        Assert.DoesNotContain("-md", decision.ExtraArgs);
    }

    [Fact]
    public void Build_MidVram_SkipsDraftLargerThanLeftover()
    {
        var draft = new LlamaDraftCandidate(
            @"D:\models\gemma-4-E2B-it-Q3.gguf",
            "gemma-4-E2B-it-Q3.gguf",
            Gb(2.72));

        var decision = LlamaLaunchPolicy.Build(
            enabled: true,
            Gpu(vramGb: 16),
            Moe26B,
            modelSizeGb: 13.27,
            modelSizeBytes: Gb(13.27),
            hasUsableGpu: true,
            [draft]);

        Assert.Equal("mid-moe-kvq8", decision.ProfileName);
        Assert.DoesNotContain("-md", decision.ExtraArgs);
    }

    [Fact]
    public void TryPickDraft_PrefersNameContainingDraft()
    {
        var picked = LlamaLaunchPolicy.TryPickDraft(
            Gpu(vramGb: 24),
            LlamaLaunchPolicy.VramBand.Plenty,
            hasUsableGpu: true,
            Moe26B,
            Gb(15),
            [
                new(@"D:\models\gemma-4-E2B-it-Q4_0.gguf", "gemma-4-E2B-it-Q4_0.gguf", Gb(3.2)),
                new(@"D:\models\gemma-4-E2B-it-draft.gguf", "gemma-4-E2B-it-draft.gguf", Gb(3.5)),
            ]);

        Assert.NotNull(picked);
        Assert.Equal("gemma-4-E2B-it-draft.gguf", picked.Value.FileName);
    }

    [Fact]
    public void TryPickDraft_SkipsMmprojAndOtherFamily()
    {
        var picked = LlamaLaunchPolicy.TryPickDraft(
            Gpu(vramGb: 24),
            LlamaLaunchPolicy.VramBand.Plenty,
            hasUsableGpu: true,
            Moe26B,
            Gb(15),
            [
                new(@"D:\models\gemma-4-mmproj.gguf", "gemma-4-mmproj.gguf", Gb(1)),
                new(@"D:\models\mixtral-8x7b-Q4_0.gguf", "mixtral-8x7b-Q4_0.gguf", Gb(4)),
            ]);

        Assert.Null(picked);
    }

    [Fact]
    public void ListDraftCandidates_SkipsMainFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var main = Path.Combine(dir, Moe26B);
            var other = Path.Combine(dir, DenseE2B);
            File.WriteAllBytes(main, new byte[200]);
            File.WriteAllBytes(other, new byte[80]);

            var listed = LlamaLaunchPolicy.ListDraftCandidates(main, [dir]);

            Assert.Single(listed);
            Assert.Equal(DenseE2B, listed[0].FileName);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData(Moe26B, true)]
    [InlineData("Mixtral-8x7B-Instruct-Q4_K_M.gguf", true)]
    [InlineData("some-MoE-model.gguf", true)]
    [InlineData(DenseE2B, false)]
    public void LooksLikeMoe_MatchesNameHints(string fileName, bool expected)
    {
        Assert.Equal(expected, LlamaLaunchPolicy.LooksLikeMoe(fileName));
    }

    [Fact]
    public void ClassifyBand_MatchesFixedThresholds()
    {
        Assert.Equal(
            LlamaLaunchPolicy.VramBand.Plenty,
            LlamaLaunchPolicy.ClassifyBand(Gpu(24), 15, hasUsableGpu: true));
        Assert.Equal(
            LlamaLaunchPolicy.VramBand.Mid,
            LlamaLaunchPolicy.ClassifyBand(Gpu(16), 15, hasUsableGpu: true));
        Assert.Equal(
            LlamaLaunchPolicy.VramBand.Tight,
            LlamaLaunchPolicy.ClassifyBand(Gpu(8), 15, hasUsableGpu: true));
        Assert.Equal(
            LlamaLaunchPolicy.VramBand.CpuOnly,
            LlamaLaunchPolicy.ClassifyBand(Cpu(32), 15, hasUsableGpu: false));
    }

    private static LlamaHardwareSnapshot Gpu(double vramGb, double ramGb = 64) =>
        new(
            IsArm64: false,
            HasNvidiaGpu: true,
            HasAmdRadeonGpu: false,
            HasQualcommAdreno: false,
            HasVulkanCapableGpu: true,
            UsesUnifiedMemory: false,
            TotalPhysicalMemoryBytes: Gb(ramGb),
            DedicatedVramBytes: Gb(vramGb));

    private static LlamaHardwareSnapshot Cpu(double ramGb) =>
        new(
            IsArm64: false,
            HasNvidiaGpu: false,
            HasAmdRadeonGpu: false,
            HasQualcommAdreno: false,
            HasVulkanCapableGpu: false,
            UsesUnifiedMemory: false,
            TotalPhysicalMemoryBytes: Gb(ramGb),
            DedicatedVramBytes: 0);

    private static long Gb(double value) => (long)(value * 1024L * 1024 * 1024);
}
