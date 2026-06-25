using System.Text.Json;

namespace LocalCompanion.Localization;

public sealed class LanguageSettingsStore
{
    private readonly string _path;

    public LanguageSettingsStore(string userDataDirectory)
    {
        Directory.CreateDirectory(userDataDirectory);
        _path = Path.Combine(userDataDirectory, "language-settings.json");
    }

    /// <summary>有効な言語が読めるときだけ true（壊れたファイルは初回選択をやり直す）。</summary>
    public bool HasSavedChoice => TryReadLanguage(out _);

    public AppLanguage Load() =>
        TryReadLanguage(out var language) ? language : AppLanguage.Japanese;

    private bool TryReadLanguage(out AppLanguage language)
    {
        language = AppLanguage.Japanese;
        if (!File.Exists(_path))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (!doc.RootElement.TryGetProperty("language", out var lang))
                return false;

            var text = lang.GetString();
            if (string.Equals(text, "en", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "english", StringComparison.OrdinalIgnoreCase))
            {
                language = AppLanguage.English;
                return true;
            }

            if (string.Equals(text, "ja", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "japanese", StringComparison.OrdinalIgnoreCase))
            {
                language = AppLanguage.Japanese;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Save(AppLanguage language)
    {
        var payload = new
        {
            language = language == AppLanguage.English ? "en" : "ja",
            updatedAt = DateTimeOffset.Now.ToString("o"),
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(payload));
    }
}
