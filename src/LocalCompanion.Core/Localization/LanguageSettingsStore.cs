using System.Text.Json;
using LocalCompanion.Services;

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
            if (!AppLanguages.TryParse(text, out language))
                return false;
            return true;
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
            language = AppLanguages.ToStorage(language),
            updatedAt = DateTimeOffset.Now.ToString("o"),
        };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(payload));
    }
}
