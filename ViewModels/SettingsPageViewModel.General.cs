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

    [ObservableProperty]
    public partial bool LlamaCppAutoUpdate { get; set; } = true;

    [ObservableProperty]
    public partial bool JuliaDecisionEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool JuliaAutoUpdate { get; set; } = true;

    public ObservableCollection<JuliaDeviceChoiceViewModel> JuliaDeviceChoices { get; } = new();

    [ObservableProperty]
    public partial JuliaDeviceChoiceViewModel? SelectedJuliaDevice { get; set; }

    public ObservableCollection<GpuBackendChoiceViewModel> GpuBackendChoices { get; } = new();

    [ObservableProperty]
    public partial GpuBackendChoiceViewModel? SelectedGpuBackend { get; set; }

    public string UiSpeechInputEnabled { get; private set; } = "";
    public string UiSpeechInputEnabledHint { get; private set; } = "";
    public string UiLlamaCppAutoUpdate { get; private set; } = "";
    public string UiLlamaCppAutoUpdateHint { get; private set; } = "";
    public string UiJuliaDecision { get; private set; } = "";
    public string UiJuliaDecisionHint { get; private set; } = "";
    public string UiJuliaDevice { get; private set; } = "";
    public string UiJuliaDeviceHint { get; private set; } = "";
    public string UiJuliaAutoUpdate { get; private set; } = "";
    public string UiJuliaAutoUpdateHint { get; private set; } = "";
    public string UiGpuBackend { get; private set; } = "";
    public string UiGpuBackendHint { get; private set; } = "";

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
    partial void OnLlamaCppAutoUpdateChanged(bool value) => PersistGeneralToggles();
    partial void OnJuliaDecisionEnabledChanged(bool value) => PersistGeneralToggles();
    partial void OnJuliaAutoUpdateChanged(bool value) => PersistGeneralToggles();
    partial void OnSelectedJuliaDeviceChanged(JuliaDeviceChoiceViewModel? value) => PersistGeneralToggles();
    partial void OnSelectedGpuBackendChanged(GpuBackendChoiceViewModel? value) => PersistGeneralToggles();

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
        LlamaCppAutoUpdate = settings.LlamaCppAutoUpdate;
        JuliaDecisionEnabled = settings.JuliaDecisionEnabled;
        JuliaAutoUpdate = settings.JuliaAutoUpdate;
        RefreshJuliaDeviceChoices(settings.JuliaDevice);
        RefreshGpuBackendChoices(settings.GpuBackendPreference);
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

    private void ApplyLlamaEngineLocalizedUi()
    {
        UiLlamaCppAutoUpdate = _loc.Get("Settings.General.LlamaAutoUpdate");
        UiLlamaCppAutoUpdateHint = _loc.Get("Settings.General.LlamaAutoUpdate.Hint");
        UiJuliaDecision = _loc.Get("Settings.General.JuliaDecision");
        UiJuliaDecisionHint = _loc.Get("Settings.General.JuliaDecision.Hint");
        UiJuliaDevice = _loc.Get("Settings.General.JuliaDevice");
        UiJuliaDeviceHint = _loc.Get("Settings.General.JuliaDevice.Hint");
        UiJuliaAutoUpdate = _loc.Get("Settings.General.JuliaAutoUpdate");
        UiJuliaAutoUpdateHint = _loc.Get("Settings.General.JuliaAutoUpdate.Hint");
        UiGpuBackend = _loc.Get("Settings.General.GpuBackend");
        UiGpuBackendHint = _loc.Get("Settings.General.GpuBackend.Hint");
        OnPropertyChanged(nameof(UiLlamaCppAutoUpdate));
        OnPropertyChanged(nameof(UiLlamaCppAutoUpdateHint));
        OnPropertyChanged(nameof(UiJuliaDecision));
        OnPropertyChanged(nameof(UiJuliaDecisionHint));
        OnPropertyChanged(nameof(UiJuliaDevice));
        OnPropertyChanged(nameof(UiJuliaDeviceHint));
        OnPropertyChanged(nameof(UiJuliaAutoUpdate));
        OnPropertyChanged(nameof(UiJuliaAutoUpdateHint));
        OnPropertyChanged(nameof(UiGpuBackend));
        OnPropertyChanged(nameof(UiGpuBackendHint));
        RefreshJuliaDeviceChoices();
        RefreshGpuBackendChoices();
    }

    private void RefreshJuliaDeviceChoices(string? selectedId = null)
    {
        var keep = JuliaDevicePreferences.Normalize(
            selectedId
            ?? SelectedJuliaDevice?.Id
            ?? _appearance.Current.JuliaDevice);
        var suppress = _suppressGeneralOptionSave;
        _suppressGeneralOptionSave = true;
        JuliaDeviceChoices.Clear();
        JuliaDeviceChoices.Add(new(JuliaDevicePreferences.Auto, _loc.Get("Settings.General.JuliaDevice.Auto")));
        JuliaDeviceChoices.Add(new(JuliaDevicePreferences.Gpu, _loc.Get("Settings.General.JuliaDevice.Gpu")));
        JuliaDeviceChoices.Add(new(JuliaDevicePreferences.Cpu, _loc.Get("Settings.General.JuliaDevice.Cpu")));
        SelectedJuliaDevice = JuliaDeviceChoices.FirstOrDefault(x =>
            string.Equals(x.Id, keep, StringComparison.OrdinalIgnoreCase))
            ?? JuliaDeviceChoices[0];
        _suppressGeneralOptionSave = suppress;
    }

    private void RefreshGpuBackendChoices(string? selectedId = null)
    {
        var keep = LlamaGpuBackendPreferences.Normalize(
            selectedId
            ?? SelectedGpuBackend?.Id
            ?? _appearance.Current.GpuBackendPreference);
        var suppress = _suppressGeneralOptionSave;
        _suppressGeneralOptionSave = true;
        GpuBackendChoices.Clear();
        GpuBackendChoices.Add(new(LlamaGpuBackendPreferences.Auto, _loc.Get("Settings.General.GpuBackend.Auto")));
        GpuBackendChoices.Add(new(LlamaGpuBackendPreferences.HipRadeon, _loc.Get("Settings.General.GpuBackend.Hip")));
        GpuBackendChoices.Add(new(LlamaGpuBackendPreferences.Vulkan, _loc.Get("Settings.General.GpuBackend.Vulkan")));
        SelectedGpuBackend = GpuBackendChoices.FirstOrDefault(x =>
            string.Equals(x.Id, keep, StringComparison.OrdinalIgnoreCase))
            ?? GpuBackendChoices[0];
        _suppressGeneralOptionSave = suppress;
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
        dto.LlamaCppAutoUpdate = LlamaCppAutoUpdate;
        dto.JuliaDecisionEnabled = JuliaDecisionEnabled;
        dto.JuliaAutoUpdate = JuliaAutoUpdate;
        dto.JuliaDevice = SelectedJuliaDevice?.Id ?? JuliaDevicePreferences.Auto;
        dto.GpuBackendPreference = SelectedGpuBackend?.Id ?? LlamaGpuBackendPreferences.Auto;
        _appearance.Save(dto);
        LayaSidecar.Apply(AppPaths.Current, JuliaDecisionEnabled);
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
        dto.LlamaCppAutoUpdate = defaults.LlamaCppAutoUpdate;
        dto.JuliaDecisionEnabled = defaults.JuliaDecisionEnabled;
        dto.JuliaAutoUpdate = defaults.JuliaAutoUpdate;
        dto.JuliaDevice = defaults.JuliaDevice;
        dto.GpuBackendPreference = defaults.GpuBackendPreference;
        var saved = _appearance.Save(dto);
        ApplyGeneralForm(saved);
        LayaSidecar.Apply(AppPaths.Current, saved.JuliaDecisionEnabled);
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
        dto.LlamaCppAutoUpdate = LlamaCppAutoUpdate;
        dto.JuliaDecisionEnabled = JuliaDecisionEnabled;
        dto.JuliaAutoUpdate = JuliaAutoUpdate;
        dto.JuliaDevice = SelectedJuliaDevice?.Id ?? JuliaDevicePreferences.Auto;
        dto.GpuBackendPreference = SelectedGpuBackend?.Id ?? LlamaGpuBackendPreferences.Auto;
        return dto;
    }
}
