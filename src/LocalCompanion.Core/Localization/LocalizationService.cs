namespace LocalCompanion.Localization;

public sealed class LocalizationService
{
    public static LocalizationService Instance { get; private set; } = null!;

    private readonly LanguageSettingsStore _store;
    private AppLanguage _current;

    public LocalizationService(LanguageSettingsStore store)
    {
        _store = store;
        _current = store.Load();
        Instance = this;
    }

    public event EventHandler? Changed;

    public AppLanguage Current => _current;

    public bool NeedsLanguageChoice => !_store.HasSavedChoice;

    public string Get(string key)
    {
        var table = LocalizationResources.For(_current);
        if (table.TryGetValue(key, out var value))
            return value;
        return key;
    }

    public string Format(string key, params object[] args) =>
        string.Format(Get(key), args);

    public void SetLanguage(AppLanguage language, bool persist = true)
    {
        var changed = _current != language;
        _current = language;
        if (persist)
            _store.Save(language);
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
