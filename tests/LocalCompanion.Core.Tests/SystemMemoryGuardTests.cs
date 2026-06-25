using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class SystemMemoryGuardTests
{
    private static long GiB(int n) => n * 1024L * 1024 * 1024;

    private const long DefaultWeightsBytes = 3_350_000_000L + 987_000_000L;
    private const long DefaultKvBytes = 8192L * 180_000L;

    [Fact]
    public void TryAssess_CpuPath_PassesOnRecommendedRam()
    {
        var snap = new LlamaHardwareSnapshot(
            false, false, false, false, false, false, GiB(16), 0);

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: false, out var note);

        Assert.True(ok);
        Assert.Null(note);
    }

    [Fact]
    public void TryAssess_CpuPath_AdvisesWhenRamIsTight()
    {
        var snap = new LlamaHardwareSnapshot(
            false, false, false, false, false, false, GiB(12), 0);

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: false, out var note);

        Assert.True(ok);
        Assert.Equal("Startup.Memory.CpuTight", note);
    }

    [Fact]
    public void TryAssess_CpuPath_FailsBelowRecommendedRam()
    {
        var snap = new LlamaHardwareSnapshot(
            false, false, false, false, false, false, GiB(8), 0);

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: false, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryAssess_UnifiedMemory_PassesOnMinimumUnifiedRam()
    {
        var snap = new LlamaHardwareSnapshot(
            true, false, false, false, false, true, GiB(16), GiB(4));

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: true, out var note);

        Assert.True(ok);
        Assert.Null(note);
    }

    [Fact]
    public void TryAssess_GpuPath_PassesWithAdequateVramAndSystemRam()
    {
        var snap = new LlamaHardwareSnapshot(
            false, true, false, false, true, false, GiB(16), GiB(8));

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: true, out var note);

        Assert.True(ok);
        Assert.Null(note);
    }

    [Fact]
    public void TryAssess_LowVramPath_PassesWithAdvisory()
    {
        var snap = new LlamaHardwareSnapshot(
            false, true, false, false, true, false, GiB(10), GiB(4));

        var ok = SystemMemoryGuard.TryAssess(
            snap, DefaultWeightsBytes, DefaultKvBytes, hasUsableGpu: true, out var note);

        Assert.True(ok);
        Assert.Equal("Startup.Memory.LowVram", note);
    }
}
