using LocalCompanion.Localization;
using Microsoft.Windows.Globalization;

namespace LocalCompanion.Services;

/// <summary>アプリ言語を WinRT UI（ファイルピッカー等）へ反映する。</summary>
public static class WinUiLanguageBridge
{
    public static void ApplyFromLocalization()
    {
        try
        {
            var tag = LocalizationService.Instance.Current switch
            {
                AppLanguage.Japanese => "ja-JP",
                _ => "en-US",
            };
            tag = ResolveAvailableLanguageTag(tag);
            ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch (Exception ex)
        {
            // unpackaged 直起動では Windows.Globalization が使えない。失敗してもアプリ本体は起動させる。
            StartupLog.Write(ex, "WinUiLanguageBridge");
        }
    }

    private static string ResolveAvailableLanguageTag(string preferredTag)
    {
        IReadOnlyList<string> available;
        try
        {
            available = ApplicationLanguages.ManifestLanguages;
        }
        catch
        {
            return preferredTag;
        }

        if (available is null || available.Count == 0)
            return preferredTag;

        foreach (var candidate in available)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (string.Equals(candidate, preferredTag, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        var prefix = preferredTag.Split('-')[0];
        foreach (var candidate in available)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return preferredTag;
    }
}
