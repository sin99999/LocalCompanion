using LocalCompanion.Localization;

namespace LocalCompanion.Core.Tests;

public sealed class AppLanguagesTests
{
    [Theory]
    [InlineData("en", AppLanguage.English)]
    [InlineData("es", AppLanguage.Spanish)]
    [InlineData("pt", AppLanguage.Portuguese)]
    [InlineData("pt-BR", AppLanguage.Portuguese)]
    [InlineData("ru", AppLanguage.Russian)]
    [InlineData("zh-Hans", AppLanguage.ChineseSimplified)]
    [InlineData("zh-CN", AppLanguage.ChineseSimplified)]
    [InlineData("ko", AppLanguage.Korean)]
    [InlineData("ja", AppLanguage.Japanese)]
    public void Parse_KnownStorage_RoundTrips(string text, AppLanguage expected)
    {
        Assert.True(AppLanguages.TryParse(text, out var parsed));
        Assert.Equal(expected, parsed);
        Assert.Equal(expected, AppLanguages.Parse(text));
    }

    [Fact]
    public void ToStorage_ThenParse_AllLanguages()
    {
        foreach (var language in AppLanguages.All)
        {
            var stored = AppLanguages.ToStorage(language);
            Assert.True(AppLanguages.TryParse(stored, out var parsed));
            Assert.Equal(language, parsed);
        }
    }

    [Fact]
    public void HelpFileSuffixes_Spanish_FallsBackToEnglishThenJapanese()
    {
        var suffixes = AppLanguages.HelpFileSuffixes(AppLanguage.Spanish).ToArray();
        Assert.Equal(["es", "en", "ja"], suffixes);
    }
}

public sealed class LocalizationResourcesTests
{
    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Spanish)]
    [InlineData(AppLanguage.Portuguese)]
    [InlineData(AppLanguage.Russian)]
    [InlineData(AppLanguage.ChineseSimplified)]
    [InlineData(AppLanguage.Korean)]
    public void For_OverlaysEveryJapaneseKey(AppLanguage language)
    {
        var japanese = LocalizationResources.For(AppLanguage.Japanese);
        var table = LocalizationResources.For(language);
        foreach (var key in japanese.Keys)
            Assert.True(table.ContainsKey(key), key);
    }

    [Fact]
    public void For_Spanish_TranslatesNavChat()
    {
        var ja = LocalizationResources.For(AppLanguage.Japanese)["Nav.Chat"];
        var es = LocalizationResources.For(AppLanguage.Spanish)["Nav.Chat"];
        Assert.NotEqual(ja, es);
        Assert.False(string.Equals(es, "Nav.Chat", StringComparison.Ordinal));
    }

    [Fact]
    public void AlreadyRunning_ExistsInAllLanguages()
    {
        foreach (var language in AppLanguages.All)
        {
            var text = LocalizationResources.For(language)["App.AlreadyRunning"];
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("App.AlreadyRunning", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SplashLanguageTitle_AllLanguagesAreDistinct()
    {
        var titles = AppLanguages.All
            .Select(language => LocalizationResources.For(language)["Splash.Language.Title"])
            .ToArray();
        Assert.Equal(AppLanguages.All.Length, titles.Distinct(StringComparer.Ordinal).Count());
    }
}

public sealed class LanguageSettingsStoreTests
{
    [Fact]
    public void SaveLoad_ChineseSimplified_RoundTrip()
    {
        var dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var store = new LanguageSettingsStore(dir);
            store.Save(AppLanguage.ChineseSimplified);
            Assert.True(store.HasSavedChoice);
            Assert.Equal(AppLanguage.ChineseSimplified, store.Load());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
