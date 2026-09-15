using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>
/// 「〇〇の見た目で」が JSON 照合と見出し抜きを間違えず、雑談の「今日の見た目」を拾わないこと。
/// </summary>
public sealed class ChatAppearanceLookupTests
{
    [Fact]
    public void ExtractSubjectNames_JapaneseCue_TakesHonorificAndLastSegment()
    {
        var names = ChatAppearanceLookup.ExtractSubjectNames(
            "私の妹の花子ちゃんの見た目で画像を作って");
        Assert.Equal(["私の妹の花子ちゃん"], names);
        Assert.Equal("花子", ChatAppearanceLookup.NormalizeName(
            names[0].Split('の', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1]));
    }

    [Fact]
    public void ExtractSubjectNames_SkipsTodayAndGenericLooks()
    {
        Assert.Empty(ChatAppearanceLookup.ExtractSubjectNames("今日の見た目どう？"));
        Assert.Empty(ChatAppearanceLookup.ExtractSubjectNames("私の見た目で"));
        Assert.Empty(ChatAppearanceLookup.ExtractSubjectNames("普通の容姿を教えて"));
        Assert.False(ChatAppearanceLookup.LooksLikeAppearanceRequest("今日の見た目どう？"));
    }

    [Fact]
    public void ExtractSubjectNames_EnglishCue()
    {
        Assert.Equal(["Example"], ChatAppearanceLookup.ExtractSubjectNames("appearance of Example please"));
        Assert.Equal(["Hanako"], ChatAppearanceLookup.ExtractSubjectNames("Hanako's appearance for an image"));
    }

    [Fact]
    public void Match_UsesFileStemWhenDisplayNameDiffers()
    {
        var presets = new[]
        {
            new CharacterAppearancePreset(
                "花子.json",
                Profile("Example", "## 容姿\n銀髪")),
        };

        var found = ChatAppearanceLookup.Match("花子ちゃん", presets);
        Assert.NotNull(found);
        Assert.Equal("Example", found.Name);
    }

    [Fact]
    public void Match_UsesProfileName()
    {
        var presets = new[]
        {
            new CharacterAppearancePreset("example.json", Profile("太郎", "黒い髪")),
        };

        var found = ChatAppearanceLookup.Match("太郎さん", presets);
        Assert.NotNull(found);
        Assert.Equal("太郎", found.Name);
    }

    [Fact]
    public void ExtractAppearanceText_TakesHeadingBody_NotTheRest()
    {
        var persona = """
            口調は元気。

            ## 容姿
            銀髪ロング、赤いリボン

            ## 口調
            ため口
            """;

        var text = ChatAppearanceLookup.ExtractAppearanceText(persona);
        Assert.Contains("銀髪ロング", text);
        Assert.DoesNotContain("ため口", text);
        Assert.DoesNotContain("口調は元気", text);
    }

    [Fact]
    public void ExtractAppearanceText_FallsBackToPersonaWhenNoHeading()
    {
        var text = ChatAppearanceLookup.ExtractAppearanceText("黒い短髪の青年。眼鏡。");
        Assert.Equal("黒い短髪の青年。眼鏡。", text);
    }

    [Fact]
    public void ExtractAppearanceText_ClampsLongPersona()
    {
        var longPersona = new string('あ', 1600);
        var text = ChatAppearanceLookup.ExtractAppearanceText(longPersona);
        Assert.Equal(1501, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void BuildWebQuery_JapaneseVsEnglish()
    {
        Assert.Contains("外見", ChatAppearanceLookup.BuildWebQuery("花子"), StringComparison.Ordinal);
        Assert.Contains("appearance", ChatAppearanceLookup.BuildWebQuery("Example"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrichAsync_LocalPreset_AttachesWithoutWeb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            _ = new LocalizationService(new LanguageSettingsStore(dir));
            var presets = new[]
            {
                new CharacterAppearancePreset("花子.json", Profile("花子", "## 容姿\n青い目")),
            };
            var req = new ChatRequestDto("花子の見た目で描いて");
            var enriched = await ChatAppearanceLookupEnricher.EnrichAsync(
                req,
                presets,
                webSearchEnabled: false,
                webSearchBaseUrl: null,
                searchTopK: 3,
                fetchTopK: 0,
                maxCharsPerUrl: 1000,
                maxAttachChars: 4000);

            Assert.True(enriched.UsedAppearanceLocal);
            Assert.False(enriched.UsedAppearanceWeb);
            Assert.Contains("青い目", enriched.AttachedText);
            Assert.DoesNotContain("見つかりませんでした", enriched.AttachedText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task EnrichAsync_UnknownName_WebOff_AttachesNotFound_DoesNotSearch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            _ = new LocalizationService(new LanguageSettingsStore(dir));
            var req = new ChatRequestDto("存在しない太郎の見た目で");
            var enriched = await ChatAppearanceLookupEnricher.EnrichAsync(
                req,
                [],
                webSearchEnabled: false,
                webSearchBaseUrl: "http://127.0.0.1:1",
                searchTopK: 3,
                fetchTopK: 1,
                maxCharsPerUrl: 1000,
                maxAttachChars: 4000);

            Assert.False(enriched.UsedAppearanceLocal);
            Assert.False(enriched.UsedAppearanceWeb);
            Assert.Contains("太郎", enriched.AttachedText);
            Assert.True(ChatAppearanceLookup.LooksLikeAppearanceRequest(req.Message));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static CharacterProfileDto Profile(string name, string persona) =>
        new(name, persona, "", 1, 0.95, 64, 4096, 512);
}
