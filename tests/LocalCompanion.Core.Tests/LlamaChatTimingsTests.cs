using System.Text.Json;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaChatTimingsTests
{
    [Fact]
    public void TryReadPredictedPerSecond_DirectField_ReturnsValue()
    {
        using var doc = JsonDocument.Parse("""{"timings":{"predicted_per_second":34.02}}""");
        var speed = LlamaChatTimings.TryReadPredictedPerSecond(doc.RootElement);
        Assert.Equal(34.02, speed);
    }

    [Fact]
    public void TryReadPredictedPerSecond_FromCountAndMs_Computes()
    {
        using var doc = JsonDocument.Parse("""{"timings":{"predicted_n":40,"predicted_ms":2000}}""");
        var speed = LlamaChatTimings.TryReadPredictedPerSecond(doc.RootElement);
        Assert.NotNull(speed);
        Assert.Equal(20.0, speed.Value, 3);
    }

    [Fact]
    public void TryReadPredictedPerSecond_Missing_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{"choices":[]}""");
        Assert.Null(LlamaChatTimings.TryReadPredictedPerSecond(doc.RootElement));
    }

    [Fact]
    public void FormatTokPerSec_OneDecimal()
    {
        Assert.Equal("12.3", LlamaChatTimings.FormatTokPerSec(12.34));
    }
}
