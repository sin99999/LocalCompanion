using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaContextPolicyTests
{
    [Fact]
    public void CapForServer_ReducesVeryLargeRequests()
    {
        Assert.Equal(8192, LlamaContextPolicy.CapForServer(8192));
        Assert.Equal(LlamaContextPolicy.StandardCap, LlamaContextPolicy.CapForServer(32768));
    }

    [Fact]
    public void CapForModel_AppliesMultimodalCapForLargeModels()
    {
        var capped = LlamaContextPolicy.CapForModel(32768, modelSizeGb: 12, hasMmproj: true);
        Assert.Equal(LlamaContextPolicy.LargeMultimodalCap, capped);
    }

    [Fact]
    public void CapForModel_LargeModelWithoutMmproj_UsesStandardCapOnly()
    {
        var capped = LlamaContextPolicy.CapForModel(32768, modelSizeGb: 12, hasMmproj: false);
        Assert.Equal(LlamaContextPolicy.StandardCap, capped);
    }

    [Fact]
    public void UiContextSliderMaximum_MatchesStandardCap()
    {
        Assert.Equal(LlamaContextPolicy.StandardCap, LlamaContextPolicy.UiContextSliderMaximum);
    }

    [Fact]
    public void EffectiveContext_UsesRunningMarkerWhenLower()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-ctx-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".last-ctx"), "8192");
            var effective = LlamaContextPolicy.EffectiveContext(16384, dir);
            Assert.Equal(8192, effective);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EffectiveContext_WithoutMarker_UsesCapForModel()
    {
        Assert.Equal(
            LlamaContextPolicy.StandardCap,
            LlamaContextPolicy.EffectiveContext(32768, toolsDirectory: null));
    }
}
