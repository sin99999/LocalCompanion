using LocalCompanion.Localization;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxSpeakerLocalizerTests
{
    [Fact]
    public void FormatDisplayName_Japanese_KeepsOriginalNames()
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", AppLanguage.Japanese);
        Assert.Contains("春日部つむぎ", name, StringComparison.Ordinal);
        Assert.Contains("ノーマル", name, StringComparison.Ordinal);
        Assert.DoesNotContain("Kasukabe", name, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDisplayName_English_UsesRomanizedName()
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", AppLanguage.English);
        Assert.Contains("Kasukabe Tsumugi", name, StringComparison.Ordinal);
        Assert.Contains("Normal", name, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDisplayName_ChineseSimplified_RomanizedName_TranslatedStyle()
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", AppLanguage.ChineseSimplified);
        Assert.Contains("Kasukabe Tsumugi", name, StringComparison.Ordinal);
        Assert.Contains("普通", name, StringComparison.Ordinal);
        Assert.DoesNotContain("春日部", name, StringComparison.Ordinal);
        Assert.DoesNotContain("Normal", name, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDisplayName_Korean_RomanizedName_TranslatedStyle()
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", AppLanguage.Korean);
        Assert.Contains("Kasukabe Tsumugi", name, StringComparison.Ordinal);
        Assert.Contains("보통", name, StringComparison.Ordinal);
        Assert.DoesNotContain("春日部", name, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDisplayName_Russian_RomanizedName_TranslatedStyle()
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", AppLanguage.Russian);
        Assert.Contains("Kasukabe Tsumugi", name, StringComparison.Ordinal);
        Assert.Contains("Обычный", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AppLanguage.Spanish, "Estándar")]
    [InlineData(AppLanguage.Portuguese, "Padrão")]
    [InlineData(AppLanguage.German, "Standard")]
    [InlineData(AppLanguage.French, "Standard")]
    public void FormatDisplayName_European_RomanizedName_TranslatedStyle(AppLanguage language, string style)
    {
        var name = VoicevoxSpeakerLocalizer.FormatDisplayName(
            8, "春日部つむぎ", "ノーマル", language);
        Assert.Contains("Kasukabe Tsumugi", name, StringComparison.Ordinal);
        Assert.Contains(style, name, StringComparison.Ordinal);
        Assert.DoesNotContain("ノーマル", name, StringComparison.Ordinal);
        Assert.DoesNotContain("春日部", name, StringComparison.Ordinal);
    }
}
