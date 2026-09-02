using System.Globalization;

namespace LocalCompanion.Localization;

/// <summary>IconMaker と同じ 10 言語。足りない文言は日本語。</summary>
public static class AppLanguages
{
    public static readonly AppLanguage[] All =
    [
        AppLanguage.Japanese,
        AppLanguage.English,
        AppLanguage.Spanish,
        AppLanguage.Portuguese,
        AppLanguage.German,
        AppLanguage.French,
        AppLanguage.Russian,
        AppLanguage.ChineseSimplified,
        AppLanguage.ChineseTraditional,
        AppLanguage.Korean,
    ];

    public static string ToStorage(AppLanguage language) => language switch
    {
        AppLanguage.English => "en",
        AppLanguage.Spanish => "es",
        AppLanguage.Portuguese => "pt",
        AppLanguage.German => "de",
        AppLanguage.French => "fr",
        AppLanguage.Russian => "ru",
        AppLanguage.ChineseSimplified => "zh-Hans",
        AppLanguage.ChineseTraditional => "zh-Hant",
        AppLanguage.Korean => "ko",
        _ => "ja",
    };

    public static string NativeName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        AppLanguage.Spanish => "Español",
        AppLanguage.Portuguese => "Português",
        AppLanguage.German => "Deutsch",
        AppLanguage.French => "Français",
        AppLanguage.Russian => "Русский",
        AppLanguage.ChineseSimplified => "简体中文",
        AppLanguage.ChineseTraditional => "繁體中文",
        AppLanguage.Korean => "한국어",
        _ => "日本語",
    };

    public static string ToBcp47(AppLanguage language) => language switch
    {
        AppLanguage.English => "en-US",
        AppLanguage.Spanish => "es-ES",
        AppLanguage.Portuguese => "pt-BR",
        AppLanguage.German => "de-DE",
        AppLanguage.French => "fr-FR",
        AppLanguage.Russian => "ru-RU",
        AppLanguage.ChineseSimplified => "zh-CN",
        AppLanguage.ChineseTraditional => "zh-TW",
        AppLanguage.Korean => "ko-KR",
        _ => "ja-JP",
    };

    public static bool TryParse(string? text, out AppLanguage language)
    {
        language = Parse(text);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var parsed = Parse(trimmed);
        if (parsed != AppLanguage.Japanese)
            return true;

        return trimmed.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("japanese", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("ja-jp", StringComparison.OrdinalIgnoreCase);
    }

    public static AppLanguage Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return AppLanguage.Japanese;

        var key = text.Trim().ToLowerInvariant();
        if (key.StartsWith("zh-hant", StringComparison.Ordinal)
            || key is "zh-tw" or "zh-hk" or "zh-mo")
            return AppLanguage.ChineseTraditional;

        return key switch
        {
            "en" or "english" or "en-us" or "en-gb" => AppLanguage.English,
            "es" or "spanish" or "español" or "es-es" or "es-mx" => AppLanguage.Spanish,
            "pt" or "pt-br" or "pt-pt" or "portuguese" or "português" => AppLanguage.Portuguese,
            "de" or "de-de" or "de-at" or "de-ch" or "german" or "deutsch" => AppLanguage.German,
            "fr" or "fr-fr" or "fr-ca" or "fr-be" or "fr-ch" or "french" or "français" => AppLanguage.French,
            "ru" or "russian" or "ru-ru" => AppLanguage.Russian,
            "zh" or "zh-hans" or "zh-cn" or "zh-sg" or "chinese" => AppLanguage.ChineseSimplified,
            "ko" or "korean" or "ko-kr" => AppLanguage.Korean,
            _ => AppLanguage.Japanese,
        };
    }

    public static AppLanguage FromUiCulture()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (TryParse(culture.Name, out var fromName) && fromName != AppLanguage.Japanese)
            return fromName;
        if (TryParse(culture.TwoLetterISOLanguageName, out var fromTwo) && fromTwo != AppLanguage.Japanese)
            return fromTwo;
        if (culture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.Japanese;
        return AppLanguage.Japanese;
    }

    /// <summary>アプリ内ヘルプ HTML の探索順（無い言語は en → ja）。</summary>
    public static IEnumerable<string> HelpFileSuffixes(AppLanguage language)
    {
        yield return ToStorage(language);
        if (language == AppLanguage.ChineseSimplified)
            yield return "zh";
        if (language != AppLanguage.English)
            yield return "en";
        if (language != AppLanguage.Japanese)
            yield return "ja";
    }
}
