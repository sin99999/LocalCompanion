namespace LocalCompanion.Localization;

/// <summary>VOICEVOX 話者コンボの表示名を、選択中の表示言語に合わせて組み立てる。</summary>
public static class VoicevoxSpeakerLocalizer
{
    public static string FormatDisplayName(int styleId, string speakerNameJa, string styleNameJa) =>
        FormatDisplayName(styleId, speakerNameJa, styleNameJa, LocalizationService.Instance.Current);

    public static string FormatDisplayName(
        int styleId,
        string speakerNameJa,
        string styleNameJa,
        AppLanguage language)
    {
        string speaker;
        string style;

        if (VoicevoxSpeakerStyleCatalog.TryGet(language, styleId, out var catalogEntry))
        {
            speaker = catalogEntry.Speaker;
            style = catalogEntry.Style;
        }
        else
        {
            speaker = VoicevoxSpeakerNameTables.TranslateSpeaker(language, speakerNameJa);
            style = VoicevoxSpeakerNameTables.TranslateStyle(language, styleNameJa);
        }

        var table = LocalizationResources.For(language);
        var format = table.TryGetValue("Voicevox.Speaker.DisplayFormat", out var value)
            ? value
            : "{0} ({1})";
        return string.Format(format, speaker, style);
    }
}
