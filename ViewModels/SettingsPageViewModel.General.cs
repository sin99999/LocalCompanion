using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.UI.Xaml;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressGeneralOptionSave;

    public ObservableCollection<string> ChatFontChoices { get; } = new();

    [ObservableProperty]
    public partial string? SelectedChatFontFamily { get; set; }

    [ObservableProperty]
    public partial double GeneralChatFontSize { get; set; } = 14;

    [ObservableProperty]
    public partial bool ConfirmHistoryDelete { get; set; } = true;

    [ObservableProperty]
    public partial string UserDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SpeechInputEnabled { get; set; }

    public string UiSpeechInputEnabled { get; private set; } = "";
    public string UiSpeechInputEnabledHint { get; private set; } = "";

    [ObservableProperty]
    public partial string GeneralStatusText { get; set; } = "";

    public Visibility GeneralStatusVisibility =>
        string.IsNullOrWhiteSpace(GeneralStatusText) ? Visibility.Collapsed : Visibility.Visible;

    partial void OnGeneralStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(GeneralStatusVisibility));

    [ObservableProperty]
    public partial string UiGeneralPreview { get; set; } = "";

    public string GeneralPreviewText { get; private set; } = "";
    public string GeneralPreviewFontFamily { get; private set; } = AppSettingsDto.DefaultChatFontFamily;

    public double GeneralPreviewScale =>
        GeneralChatFontSize / AppSettingsDto.DefaultChatFontSize;

    partial void OnGeneralChatFontSizeChanged(double value)
    {
        RefreshSliderLabels();
        UpdateGeneralPreview();
        OnPropertyChanged(nameof(GeneralPreviewScale));
    }

    partial void OnSelectedChatFontFamilyChanged(string? value) => UpdateGeneralPreview();

    partial void OnConfirmHistoryDeleteChanged(bool value) => PersistGeneralToggles();
    partial void OnSpeechInputEnabledChanged(bool value) => PersistGeneralToggles();

    private void LoadGeneralSettings()
    {
        ApplyGeneralForm(_appearance.Current);
    }

    private void RefreshChatFontChoices()
    {
        var selected = SelectedChatFontFamily ?? _appearance.Current.ChatFontFamily;
        ChatFontChoices.Clear();
        foreach (var font in SystemFontCatalog.ListFontFamilies())
            ChatFontChoices.Add(font);
        SelectedChatFontFamily = ChatFontChoices.FirstOrDefault(f =>
            string.Equals(f, selected, StringComparison.OrdinalIgnoreCase))
            ?? selected;
        UpdateGeneralPreview();
    }

    private void ApplyGeneralForm(AppSettingsDto settings)
    {
        _suppressGeneralOptionSave = true;
        ConfirmHistoryDelete = settings.ConfirmHistoryDelete;
        UserDisplayName = settings.UserDisplayName;
        SpeechInputEnabled = settings.SpeechInputEnabled;
        GeneralChatFontSize = settings.ChatFontSize;
        SelectedChatFontFamily = ChatFontChoices.FirstOrDefault(f =>
            string.Equals(f, settings.ChatFontFamily, StringComparison.OrdinalIgnoreCase))
            ?? settings.ChatFontFamily;
        _suppressGeneralOptionSave = false;
        RefreshSliderLabels();
        UpdateGeneralPreview();
    }

    private void ApplySpeechInputLocalizedUi()
    {
        UiSpeechInputEnabled = _loc.Get("Settings.SpeechInput.Enabled");
        UiSpeechInputEnabledHint = _loc.Get("Settings.SpeechInput.Enabled.Hint");
        OnPropertyChanged(nameof(UiSpeechInputEnabled));
        OnPropertyChanged(nameof(UiSpeechInputEnabledHint));
    }

    private void UpdateGeneralPreview()
    {
        GeneralPreviewText = _loc.Get("Settings.General.Preview.Sample");
        GeneralPreviewFontFamily = SelectedChatFontFamily ?? AppSettingsDto.DefaultChatFontFamily;
        OnPropertyChanged(nameof(GeneralPreviewText));
        OnPropertyChanged(nameof(GeneralPreviewFontFamily));
        OnPropertyChanged(nameof(GeneralPreviewScale));
    }

    private void PersistGeneralToggles()
    {
        if (_suppressGeneralOptionSave)
            return;

        var c = _appearance.Current;
        _appearance.Save(new AppSettingsDto
        {
            ConfirmHistoryDelete = ConfirmHistoryDelete,
            ThemeMode = AppThemeModes.Dark,
            ChatFontFamily = c.ChatFontFamily,
            ChatFontSize = c.ChatFontSize,
            UserDisplayName = c.UserDisplayName,
            RagUseHtmlMarkdown = c.RagUseHtmlMarkdown,
            RagUseLlmStructurer = c.RagUseLlmStructurer,
            RagSaveStructurerCache = c.RagSaveStructurerCache,
            RagUsePdfLayoutReader = c.RagUsePdfLayoutReader,
            MemoryEnabled = c.MemoryEnabled,
            MemoryAutoExtractOnClose = c.MemoryAutoExtractOnClose,
            ChatSearchEnabled = c.ChatSearchEnabled,
            SpeechInputEnabled = SpeechInputEnabled,
        });
    }

    [RelayCommand]
    private void SaveGeneral()
    {
        SetGeneralStatus(null);
        var saved = _appearance.Save(BuildGeneralSettingsDto());
        ApplyGeneralForm(saved);
        SetGeneralStatus("Settings.General.Saved");
    }

    [RelayCommand]
    private void ResetGeneral()
    {
        SetGeneralStatus(null);
        var saved = _appearance.Save(AppSettingsDto.CreateDefault());
        ApplyGeneralForm(saved);
        SetGeneralStatus("Settings.General.ResetDone");
    }

    private AppSettingsDto BuildGeneralSettingsDto() => new()
    {
        ConfirmHistoryDelete = ConfirmHistoryDelete,
        ThemeMode = AppThemeModes.Dark,
        ChatFontFamily = SelectedChatFontFamily ?? AppSettingsDto.DefaultChatFontFamily,
        ChatFontSize = GeneralChatFontSize,
        UserDisplayName = UserDisplayName,
        RagUseHtmlMarkdown = _appearance.Current.RagUseHtmlMarkdown,
        RagUseLlmStructurer = _appearance.Current.RagUseLlmStructurer,
        RagSaveStructurerCache = _appearance.Current.RagSaveStructurerCache,
        RagUsePdfLayoutReader = _appearance.Current.RagUsePdfLayoutReader,
        MemoryEnabled = _appearance.Current.MemoryEnabled,
        MemoryAutoExtractOnClose = _appearance.Current.MemoryAutoExtractOnClose,
        ChatSearchEnabled = _appearance.Current.ChatSearchEnabled,
        SpeechInputEnabled = SpeechInputEnabled,
    };
}
