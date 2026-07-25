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
        var dto = c.Clone();
        dto.ConfirmHistoryDelete = ConfirmHistoryDelete;
        dto.ThemeMode = AppThemeModes.Dark;
        dto.SpeechInputEnabled = SpeechInputEnabled;
        _appearance.Save(dto);
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
        // 基本タブの項目だけ戻す（記憶・RAG・キャラ育てる 等は触らない）
        var defaults = AppSettingsDto.CreateDefault();
        var dto = _appearance.Current.Clone();
        dto.ConfirmHistoryDelete = defaults.ConfirmHistoryDelete;
        dto.ThemeMode = AppThemeModes.Dark;
        dto.ChatFontFamily = defaults.ChatFontFamily;
        dto.ChatFontSize = defaults.ChatFontSize;
        dto.UserDisplayName = defaults.UserDisplayName;
        dto.SpeechInputEnabled = defaults.SpeechInputEnabled;
        var saved = _appearance.Save(dto);
        ApplyGeneralForm(saved);
        SetGeneralStatus("Settings.General.ResetDone");
    }

    private AppSettingsDto BuildGeneralSettingsDto()
    {
        var dto = _appearance.Current.Clone();
        dto.ConfirmHistoryDelete = ConfirmHistoryDelete;
        dto.ThemeMode = AppThemeModes.Dark;
        dto.ChatFontFamily = SelectedChatFontFamily ?? AppSettingsDto.DefaultChatFontFamily;
        dto.ChatFontSize = GeneralChatFontSize;
        dto.UserDisplayName = UserDisplayName;
        dto.SpeechInputEnabled = SpeechInputEnabled;
        return dto;
    }
}
