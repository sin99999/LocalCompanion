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

    public string UiMemoryEnabled { get; private set; } = "";
    public string UiMemoryEnabledHint { get; private set; } = "";
    public string UiMemoryAutoExtract { get; private set; } = "";
    public string UiMemoryAutoExtractHint { get; private set; } = "";

    partial void OnMemoryEnabledChanged(bool value) => SaveMemorySearchOptions();
    partial void OnMemoryAutoExtractOnCloseChanged(bool value) => SaveMemorySearchOptions();

    private void LoadMemorySearchOptions()
    {
        _suppressMemoryOptionSave = true;
        var s = _appearance.Current;
        MemoryEnabled = s.MemoryEnabled;
        MemoryAutoExtractOnClose = s.MemoryAutoExtractOnClose;
        _suppressMemoryOptionSave = false;
    }

    private void ApplyMemoryLocalizedUi()
    {
        UiTabMemory = _loc.Get("Settings.Tab.Memory");
        UiMemoryEnabled = _loc.Get("Settings.Memory.Enabled");
        UiMemoryEnabledHint = _loc.Get("Settings.Memory.Enabled.Hint");
        UiMemoryAutoExtract = _loc.Get("Settings.Memory.AutoExtract");
        UiMemoryAutoExtractHint = _loc.Get("Settings.Memory.AutoExtract.Hint");
        OnPropertyChanged(nameof(UiTabMemory));
        OnPropertyChanged(nameof(UiMemoryEnabled));
        OnPropertyChanged(nameof(UiMemoryEnabledHint));
        OnPropertyChanged(nameof(UiMemoryAutoExtract));
        OnPropertyChanged(nameof(UiMemoryAutoExtractHint));
    }

    private void SaveMemorySearchOptions()
    {
        if (_suppressMemoryOptionSave)
            return;

        var c = _appearance.Current;
        var dto = c.Clone();
        dto.MemoryEnabled = MemoryEnabled;
        dto.MemoryAutoExtractOnClose = MemoryAutoExtractOnClose;
        _appearance.Save(dto);
    }
}
