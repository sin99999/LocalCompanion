using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressMemoryOptionSave;
    public ObservableCollection<UserMemoryRecord> MemoryItems { get; } = new();

    [ObservableProperty]
    public partial bool MemoryEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MemoryAutoExtractOnClose { get; set; } = true;

    [ObservableProperty]
    public partial bool ChatSearchEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool SpeechInputEnabled { get; set; }

    [ObservableProperty]
    public partial string NewMemoryText { get; set; } = "";

    public string UiMemoryEnabled { get; private set; } = "";
    public string UiMemoryEnabledHint { get; private set; } = "";
    public string UiMemoryAutoExtract { get; private set; } = "";
    public string UiMemoryAutoExtractHint { get; private set; } = "";
    public string UiMemoryManual { get; private set; } = "";
    public string UiMemoryAdd { get; private set; } = "";
    public string UiMemoryList { get; private set; } = "";
    public string UiChatSearchEnabled { get; private set; } = "";
    public string UiChatSearchEnabledHint { get; private set; } = "";
    public string UiSpeechInputEnabled { get; private set; } = "";
    public string UiSpeechInputEnabledHint { get; private set; } = "";

    partial void OnMemoryEnabledChanged(bool value) => SaveMemorySearchOptions();
    partial void OnMemoryAutoExtractOnCloseChanged(bool value) => SaveMemorySearchOptions();
    partial void OnChatSearchEnabledChanged(bool value) => SaveMemorySearchOptions();
    partial void OnSpeechInputEnabledChanged(bool value) => SaveMemorySearchOptions();

    private void LoadMemorySearchOptions()
    {
        _suppressMemoryOptionSave = true;
        var s = _appearance.Current;
        MemoryEnabled = s.MemoryEnabled;
        MemoryAutoExtractOnClose = s.MemoryAutoExtractOnClose;
        ChatSearchEnabled = s.ChatSearchEnabled;
        SpeechInputEnabled = s.SpeechInputEnabled;
        _suppressMemoryOptionSave = false;
        RefreshMemoryList();
    }

    private void RefreshMemoryList()
    {
        MemoryItems.Clear();
        if (!MemoryEnabled)
            return;

        foreach (var item in _memory.List(80))
            MemoryItems.Add(item);
    }

    [RelayCommand]
    private async Task AddMemoryAsync()
    {
        var text = NewMemoryText.Trim();
        if (text.Length == 0)
            return;

        await _memory.AddAsync(text, memoryPath: "manual");
        NewMemoryText = "";
        RefreshMemoryList();
    }

    [RelayCommand]
    private void DeleteMemory(UserMemoryRecord? record)
    {
        if (record is null)
            return;

        _memory.Delete(record.Id);
        RefreshMemoryList();
    }

    private void ApplyMemoryLocalizedUi()
    {
        UiTabMemory = _loc.Get("Settings.Tab.Memory");
        UiMemoryEnabled = _loc.Get("Settings.Memory.Enabled");
        UiMemoryEnabledHint = _loc.Get("Settings.Memory.Enabled.Hint");
        UiMemoryAutoExtract = _loc.Get("Settings.Memory.AutoExtract");
        UiMemoryAutoExtractHint = _loc.Get("Settings.Memory.AutoExtract.Hint");
        UiMemoryManual = _loc.Get("Settings.Memory.Manual");
        UiMemoryAdd = _loc.Get("Settings.Memory.Add");
        UiMemoryList = _loc.Get("Settings.Memory.List");
        UiChatSearchEnabled = _loc.Get("Settings.ChatSearch.Enabled");
        UiChatSearchEnabledHint = _loc.Get("Settings.ChatSearch.Enabled.Hint");
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
            ChatSearchEnabled = ChatSearchEnabled,
            SpeechInputEnabled = SpeechInputEnabled,
        });
        RefreshMemoryList();
    }
}
