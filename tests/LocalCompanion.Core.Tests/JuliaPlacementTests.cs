using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public class JuliaPlacementTests
{
    private const long Model = 1_000_000_000;
    private const long Headroom = JuliaPlacement.HeadroomBytes;

    [Fact]
    public void Auto_UsesGpu_WhenTheWholeWeightFits()
    {
        var decision = JuliaPlacement.Decide("auto", Model, Model + Headroom, gpuServerAvailable: true);
        Assert.True(decision.UseGpu);
        Assert.Equal("fits", decision.Reason);
    }

    [Fact]
    public void Auto_StaysOnCpu_WhenFreeMemoryIsShortOrUnknown()
    {
        var shortMemory = JuliaPlacement.Decide("auto", Model, Model + Headroom - 1, true);
        var unknown = JuliaPlacement.Decide("auto", Model, null, true);
        Assert.False(shortMemory.UseGpu);
        Assert.Equal("vram-short", shortMemory.Reason);
        Assert.False(unknown.UseGpu);
        Assert.Equal("free-vram-unknown", unknown.Reason);
    }

    [Fact]
    public void Cpu_StaysOnCpu_EvenWhenMemoryIsFree()
    {
        var decision = JuliaPlacement.Decide("CPU", Model, Model * 4, true);
        Assert.False(decision.UseGpu);
        Assert.Equal("cpu", decision.Reason);
    }

    [Fact]
    public void Gpu_UsesTheCard_WhenAGpuServerExists()
    {
        var decision = JuliaPlacement.Decide("gpu", Model, 0, true);
        Assert.True(decision.UseGpu);
        Assert.Equal("gpu", decision.Reason);
    }

    [Fact]
    public void Gpu_StaysOnCpu_WhenNoGpuServerExists()
    {
        var decision = JuliaPlacement.Decide("gpu", Model, Model * 4, false);
        Assert.False(decision.UseGpu);
        Assert.Equal("no-gpu-server", decision.Reason);
    }

    [Fact]
    public void UnknownPreference_IsAuto()
    {
        Assert.Equal(JuliaDevicePreferences.Auto, JuliaDevicePreferences.Normalize("nope"));
        Assert.Equal(JuliaDevicePreferences.Gpu, JuliaDevicePreferences.Normalize(" GPU "));
    }

    [Fact]
    public void Update_DownloadsOnlyWhenMissingOrChanged()
    {
        Assert.Equal(JuliaModelAction.Download, JuliaPlacement.DecideUpdate(false, false, null, null));
        Assert.Equal(JuliaModelAction.Keep, JuliaPlacement.DecideUpdate(false, true, "old", "new"));
        Assert.Equal(JuliaModelAction.Keep, JuliaPlacement.DecideUpdate(true, true, "same", null));
        Assert.Equal(JuliaModelAction.Remember, JuliaPlacement.DecideUpdate(true, true, null, "etag"));
        Assert.Equal(JuliaModelAction.Download, JuliaPlacement.DecideUpdate(true, true, "old", "new"));
        Assert.Equal(JuliaModelAction.Keep, JuliaPlacement.DecideUpdate(true, true, "same", "same"));
    }
}
