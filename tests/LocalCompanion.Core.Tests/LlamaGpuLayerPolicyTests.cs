using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaGpuLayerPolicyTests
{
    [Fact]
    public void BuildLayerAttempts_WithoutGpu_ReturnsCpuOnly()
    {
        Assert.Equal([0], LlamaGpuLayerPolicy.BuildLayerAttempts(99, hasUsableGpu: false));
        Assert.Equal([0], LlamaGpuLayerPolicy.BuildLayerAttempts(0, hasUsableGpu: true));
    }

    [Fact]
    public void BuildLayerAttempts_FullGpu_IncludesDescendingStepsEndingAtZero()
    {
        var attempts = LlamaGpuLayerPolicy.BuildLayerAttempts(99, hasUsableGpu: true);

        Assert.Equal([99, 48, 24, 12, 0], attempts);
    }

    [Fact]
    public void BuildLayerAttempts_LowConfigured_ClampsAndDeduplicates()
    {
        var attempts = LlamaGpuLayerPolicy.BuildLayerAttempts(20, hasUsableGpu: true);

        Assert.Equal([20, 12, 0], attempts);
    }
}
