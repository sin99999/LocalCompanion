using LocalCompanion.Models;

namespace LocalCompanion.Core.Tests;

public sealed class CharacterSamplingLimitsTests
{
    [Fact]
    public void SnapMaxOutputTokens_CapsAtHalfContext()
    {
        var cap = CharacterSamplingLimits.MaxOutputTokensCapForContext(8192);
        Assert.Equal(4096, cap);

        var snapped = CharacterSamplingLimits.SnapMaxOutputTokens(7000, 8192);
        Assert.True(snapped <= cap);
        Assert.Equal(4096, snapped);
    }

    [Fact]
    public void SnapMaxOutputTokens_UsesDefaultWhenZero()
    {
        var snapped = CharacterSamplingLimits.SnapMaxOutputTokens(0, 16384);
        Assert.Equal(CharacterDefaults.MaxOutputTokens, snapped);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var raw = new CharacterProfileDto(
            "Test",
            "persona",
            "",
            9.0,
            -1,
            999,
            999999,
            99999);

        var normalized = CharacterSamplingLimits.Normalize(raw);

        Assert.Equal(CharacterSamplingLimits.TemperatureMax, normalized.Temperature);
        Assert.Equal(CharacterSamplingLimits.TopPMin, normalized.TopP);
        Assert.Equal(CharacterSamplingLimits.TopKMax, normalized.TopK);
        Assert.True(normalized.ContextLength <= CharacterSamplingLimits.ContextLengthMax);
        Assert.True(normalized.MaxOutputTokens <= CharacterSamplingLimits.MaxOutputTokensCapForContext(normalized.ContextLength));
    }
}
