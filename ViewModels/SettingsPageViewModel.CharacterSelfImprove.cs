using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressCharacterSelfImproveSave;

    [ObservableProperty]
    public partial bool CharacterSelfImproveEnabled { get; set; }

    public string UiCharacterSelfImproveEnabled { get; private set; } = "";
    public string UiCharacterSelfImproveEnabledHint { get; private set; } = "";

    partial void OnCharacterSelfImproveEnabledChanged(bool value) => PersistCharacterSelfImprove();

    private void LoadCharacterSelfImproveOption()
    {
        _suppressCharacterSelfImproveSave = true;
        CharacterSelfImproveEnabled = _appearance.Current.CharacterSelfImproveEnabled;
        _suppressCharacterSelfImproveSave = false;
    }

    private void ApplyCharacterSelfImproveLocalizedUi()
    {
        UiCharacterSelfImproveEnabled = _loc.Get("Settings.Character.SelfImprove.Enabled");
        UiCharacterSelfImproveEnabledHint = _loc.Get("Settings.Character.SelfImprove.Enabled.Hint");
        OnPropertyChanged(nameof(UiCharacterSelfImproveEnabled));
        OnPropertyChanged(nameof(UiCharacterSelfImproveEnabledHint));
    }

    private void PersistCharacterSelfImprove()
    {
        if (_suppressCharacterSelfImproveSave)
            return;

        var dto = _appearance.Current.Clone();
        dto.CharacterSelfImproveEnabled = CharacterSelfImproveEnabled;
        _appearance.Save(dto);
    }
}
