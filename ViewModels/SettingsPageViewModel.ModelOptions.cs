using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressModelOptionSave;

    [ObservableProperty]
    public partial bool ShowChatTokenSpeed { get; set; }

    public string UiModelShowTokenSpeed { get; private set; } = "";
    public string UiModelShowTokenSpeedHint { get; private set; } = "";
    public string UiModelSpecDraftMtp { get; private set; } = "";
    public string UiModelSpecDraftMtpHint { get; private set; } = "";

    private void LoadModelOptions()
    {
        _suppressModelOptionSave = true;
        ShowChatTokenSpeed = _appearance.Current.ShowChatTokenSpeed;
        _suppressModelOptionSave = false;
    }

    private void ApplyModelOptionsLocalizedUi()
    {
        UiModelShowTokenSpeed = _loc.Get("Settings.Model.ShowTokenSpeed");
        UiModelShowTokenSpeedHint = _loc.Get("Settings.Model.ShowTokenSpeed.Hint");
        UiModelSpecDraftMtp = _loc.Get("Settings.Model.SpecDraftMtp");
        UiModelSpecDraftMtpHint = _loc.Get("Settings.Model.SpecDraftMtp.Hint");
        OnPropertyChanged(nameof(UiModelShowTokenSpeed));
        OnPropertyChanged(nameof(UiModelShowTokenSpeedHint));
        OnPropertyChanged(nameof(UiModelSpecDraftMtp));
        OnPropertyChanged(nameof(UiModelSpecDraftMtpHint));
    }

    partial void OnShowChatTokenSpeedChanged(bool value) => PersistModelOptions();

    private void PersistModelOptions()
    {
        if (_suppressModelOptionSave)
            return;

        var dto = _appearance.Current.Clone();
        dto.ShowChatTokenSpeed = ShowChatTokenSpeed;
        _appearance.Save(dto);
    }
}
