using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaHardwareProfileTests
{
    private static long GiB(int n) => n * 1024L * 1024 * 1024;

    [Theory]
    [InlineData(true, true, false, false, false, "opencl-adreno")]
    [InlineData(true, false, false, false, false, "cpu")]
    [InlineData(false, false, true, false, false, "cuda")]
    [InlineData(false, false, false, true, false, "hip-radeon")]
    [InlineData(false, false, false, false, true, "vulkan")]
    [InlineData(false, false, false, false, false, "cpu")]
    public void ResolvePreferredVariant_SelectsExpectedBackend(
        bool arm64,
        bool adreno,
        bool nvidia,
        bool amd,
        bool vulkan,
        string expected)
    {
        var snap = new LlamaHardwareSnapshot(
            arm64,
            nvidia,
            amd,
            adreno,
            vulkan,
            UsesUnifiedMemory: arm64,
            GiB(16),
            GiB(8));

        Assert.Equal(expected, LlamaHardwareProfile.ResolvePreferredVariant(snap));
    }

    [Fact]
    public void BuildInstallFallbackChain_Cuda_IncludesVulkanThenCpu()
    {
        var snap = new LlamaHardwareSnapshot(
            false, true, false, false, true, false, GiB(16), GiB(8));

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain("cuda", snap);

        Assert.Equal(["cuda", "vulkan", "cpu"], chain);
    }

    [Fact]
    public void BuildInstallFallbackChain_CudaWithoutVulkan_SkipsVulkan()
    {
        var snap = new LlamaHardwareSnapshot(
            false, true, false, false, false, false, GiB(16), GiB(8));

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain("cuda", snap);

        Assert.Equal(["cuda", "cpu"], chain);
    }

    [Fact]
    public void BuildInstallFallbackChain_HipRadeon_IncludesVulkanThenCpu()
    {
        var snap = new LlamaHardwareSnapshot(
            false, false, true, false, true, false, GiB(16), GiB(8));

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain("hip-radeon", snap);

        Assert.Equal(["hip-radeon", "vulkan", "cpu"], chain);
    }

    [Fact]
    public void BuildInstallFallbackChain_Arm64_AddsCpuOnly()
    {
        var snap = new LlamaHardwareSnapshot(
            true, false, false, true, false, true, GiB(16), GiB(4));

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain("opencl-adreno", snap);

        Assert.Equal(["opencl-adreno", "cpu"], chain);
    }
}
