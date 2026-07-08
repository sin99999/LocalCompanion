using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressMemoryOptionSave;

    [ObservableProperty]
    public partial bool MemoryEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MemoryAutoExtractOnClose { get; set; } = true;

    [ObservableProperty]
    public partial bool SpeechInputEnabled { get; set; }

    public string UiMemoryEnabled { get; private set; } = "";
    public string UiMemoryEnabledHint { get; private set; } = "";
    public string UiMemoryAutoExtract { get; private set; } = "";
    public string UiMemoryAutoExtractHint { get; private set; } = "";
    public string UiSpeechInputEnabled { get; private set; } = "";
    public string UiSpeechInputEnabledHint { get; private set; } = "";

    partial void OnMemoryEnabledChanged(bool value) => SaveMemorySearchOptions();
    partial void OnMemoryAutoExtractOnCloseChanged(bool value) => SaveMemorySearchOptions();
    partial void OnSpeechInputEnabledChanged(bool value) => SaveMemorySearchOptions();

    private void LoadMemorySearchOptions()
    {
        _suppressMemoryOptionSave = true;
        var s = _appearance.Current;
        MemoryEnabled = s.MemoryEnabled;
        MemoryAutoExtractOnClose = s.MemoryAutoExtractOnClose;
        SpeechInputEnabled = s.SpeechInputEnabled;
        _suppressMemoryOptionSave = false;
    }

    private void ApplyMemoryLocalizedUi()
    {
        UiTabMemory = _loc.Get("Settings.Tab.Memory");
        UiMemoryEnabled = _loc.Get("Settings.Memory.Enabled");
        UiMemoryEnabledHint = _loc.Get("Settings.Memory.Enabled.Hint");
        UiMemoryAutoExtract = _loc.Get("Settings.Memory.AutoExtract");
        UiMemoryAutoExtractHint = _loc.Get("Settings.Memory.AutoExtract.Hint");
        UiSpeechInputEnabled = _loc.Get("Settings.SpeechInput.Enabled");
        UiSpeechInputEnabledHint = _loc.Get("Settings.SpeechInput.Enabled.Hint");
    }

    private void SaveMemorySearchOptions()
    {
        if (_suppressMemoryOptionSave)
            return;

        var c = _appearance.Current;
        _appearance.Save(new AppSettingsDto
        {
            ConfirmHistoryDelete = c.ConfirmHistoryDelete,
            ThemeMode = c.ThemeMode,
            ChatFontFamily = c.ChatFontFamily,
            ChatFontSize = c.ChatFontSize,
            UserDisplayName = c.UserDisplayName,
            RagUseHtmlMarkdown = c.RagUseHtmlMarkdown,
            RagUseLlmStructurer = c.RagUseLlmStructurer,
            RagSaveStructurerCache = c.RagSaveStructurerCache,
            RagUsePdfLayoutReader = c.RagUsePdfLayoutReader,
            MemoryEnabled = MemoryEnabled,
            MemoryAutoExtractOnClose = MemoryAutoExtractOnClose,
            ChatSearchEnabled = false,
            SpeechInputEnabled = SpeechInputEnabled,
        });
    }
}
