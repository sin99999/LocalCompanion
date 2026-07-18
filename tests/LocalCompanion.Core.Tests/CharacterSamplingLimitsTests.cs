using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class CharacterSamplingLimitsTests
{
    [Fact]
    public void Defaults_MatchGemma4Recommendations()
    {
        Assert.Equal(1.0, CharacterDefaults.Temperature);
        Assert.Equal(0.95, CharacterDefaults.TopP);
        Assert.Equal(64, CharacterDefaults.TopK);
        Assert.Equal(16384, CharacterDefaults.ContextLength);
        Assert.Equal(4096, CharacterDefaults.MaxOutputTokens);
        Assert.True(CharacterSamplingLimits.MaxOutputTokensMax >= 16384);
    }

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

    [Fact]
    public void ContextLengthMax_MatchesLlamaStandardCap()
    {
        Assert.Equal(LlamaContextPolicy.StandardCap, CharacterSamplingLimits.ContextLengthMax);
    }
}
