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
}
