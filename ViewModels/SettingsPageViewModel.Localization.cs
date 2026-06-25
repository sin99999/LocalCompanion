using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Localization;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private readonly LocalizationService _loc = LocalizationService.Instance;

    [ObservableProperty] public partial string UiErrorTitle { get; set; }
    [ObservableProperty] public partial string UiTabGeneral { get; set; }
    [ObservableProperty] public partial string UiTabModel { get; set; }
    [ObservableProperty] public partial string UiTabCharacter { get; set; }
    [ObservableProperty] public partial string UiTabRag { get; set; }
    [ObservableProperty] public partial string UiTabVoicevox { get; set; }
    [ObservableProperty] public partial string UiGeneralFontFamily { get; set; }
    [ObservableProperty] public partial string UiGeneralUserDisplayName { get; set; }
    [ObservableProperty] public partial string UiGeneralUserDisplayNameHint { get; set; }
    [ObservableProperty] public partial string UiGeneralConfirmHistoryDelete { get; set; }
    [ObservableProperty] public partial string UiGeneralConfirmHistoryDeleteHint { get; set; }
    [ObservableProperty] public partial string UiGeneralSave { get; set; }
    [ObservableProperty] public partial string UiGeneralReset { get; set; }
    [ObservableProperty] public partial string UiModelRuntimeStatus { get; set; }
    [ObservableProperty] public partial string UiModelChatGguf { get; set; }
    [ObservableProperty] public partial string UiModelMmprojHint { get; set; }
    [ObservableProperty] public partial string UiModelApply { get; set; }
    [ObservableProperty] public partial string UiModelAdditionalFolder { get; set; }
    [ObservableProperty] public partial string UiModelAdditionalFolderBrowse { get; set; }
    [ObservableProperty] public partial string UiModelAdditionalFolderClear { get; set; }
    [ObservableProperty] public partial string UiModelAdditionalFolderHint { get; set; }
    [ObservableProperty] public partial string UiCharacterRegistered { get; set; }
    [ObservableProperty] public partial string UiCharacterName { get; set; }
    [ObservableProperty] public partial string UiCharacterPersona { get; set; }
    [ObservableProperty] public partial string UiApplyCharacterDefaults { get; set; }
    [ObservableProperty] public partial string UiCharacterTemperatureHint { get; set; }
    [ObservableProperty] public partial string UiCharacterTopPHint { get; set; }
    [ObservableProperty] public partial string UiCharacterTopKHint { get; set; }
    [ObservableProperty] public partial string UiCharacterContextLengthHint { get; set; }
    [ObservableProperty] public partial string UiCharacterMaxOutputTokensHint { get; set; }
    [ObservableProperty] public partial string UiSave { get; set; }
    [ObservableProperty] public partial string UiDelete { get; set; }
    [ObservableProperty] public partial string UiOn { get; set; }
    [ObservableProperty] public partial string UiOff { get; set; }
    [ObservableProperty] public partial string UiRagDescription { get; set; }
    [ObservableProperty] public partial string UiRagSourceUseHint { get; set; }
    [ObservableProperty] public partial string UiRagChunkCountPrefix { get; set; }
    [ObservableProperty] public partial string UiRagIngestFile { get; set; }
    [ObservableProperty] public partial string UiRagIngestFolder { get; set; }
    [ObservableProperty] public partial string UiVoicevoxEnabled { get; set; }
    [ObservableProperty] public partial string UiVoicevoxAutoSpeak { get; set; }
    [ObservableProperty] public partial string UiVoicevoxSpeakInJapanesePronunciation { get; set; }
    [ObservableProperty] public partial string UiVoicevoxSpeakInJapanesePronunciationHint { get; set; }
    [ObservableProperty] public partial string UiVoicevoxSpeaker { get; set; }
    [ObservableProperty] public partial string UiVoicevoxSpeakerPlaceholder { get; set; }
    [ObservableProperty] public partial string UiVoicevoxSave { get; set; }
    [ObservableProperty] public partial string VoicevoxPoweredByNoteText { get; set; } = "";

    private sealed record LocalizedStatusEntry(string Key, object[]? Args);

    private LocalizedStatusEntry? _generalStatus;
    private LocalizedStatusEntry? _modelStatus;
    private LocalizedStatusEntry? _characterStatus;
    private LocalizedStatusEntry? _ragStatus;
    private LocalizedStatusEntry? _voicevoxStatus;
    private Exception? _ragErrorException;
    private Exception? _modelErrorException;
    private Exception? _characterErrorException;

    private string ResolveStatus(LocalizedStatusEntry? entry) =>
        entry is null
            ? ""
            : entry.Args is { Length: > 0 }
                ? _loc.Format(entry.Key, entry.Args)
                : _loc.Get(entry.Key);

    private void SetStatus(
        ref LocalizedStatusEntry? field,
        Action<string> assign,
        string? key,
        params object[] args)
    {
        if (key is null)
        {
            field = null;
            assign("");
            return;
        }

        field = args.Length > 0 ? new LocalizedStatusEntry(key, args) : new LocalizedStatusEntry(key, null);
        assign(ResolveStatus(field));
    }

    private void SetGeneralStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _generalStatus, v => GeneralStatusText = v, localizationKey, args);

    private void SetModelStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _modelStatus, v => ModelStatusText = v, localizationKey, args);

    private void SetCharacterStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _characterStatus, v => CharacterStatusText = v, localizationKey, args);

    private void SetRagStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _ragStatus, v => RagStatusText = v, localizationKey, args);

    private void SetVoicevoxStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _voicevoxStatus, v => VoicevoxStatusText = v, localizationKey, args);

    private void RefreshLocalizedStatusTexts()
    {
        GeneralStatusText = ResolveStatus(_generalStatus);
        ModelStatusText = ResolveStatus(_modelStatus);
        CharacterStatusText = ResolveStatus(_characterStatus);
        RagStatusText = ResolveStatus(_ragStatus);
        VoicevoxStatusText = ResolveStatus(_voicevoxStatus);
        RefreshLocalizedErrorTexts();
    }

    private void RefreshLocalizedErrorTexts()
    {
        if (_ragErrorException is not null)
            RagErrorText = UserFacingErrorLocalizer.Localize(_ragErrorException);
        if (_modelErrorException is not null)
            ModelErrorText = UserFacingErrorLocalizer.Localize(_modelErrorException);
        if (_characterErrorException is not null)
            CharacterErrorText = UserFacingErrorLocalizer.Localize(_characterErrorException);
    }

    private void SetRagError(Exception ex)
    {
        _ragErrorException = ex;
        RagHasError = true;
        RagErrorText = UserFacingErrorLocalizer.Localize(ex);
    }

    private void ClearRagError()
    {
        _ragErrorException = null;
        RagHasError = false;
        RagErrorText = "";
    }

    private void SetModelError(Exception ex)
    {
        _modelErrorException = ex;
        ModelHasError = true;
        ModelErrorText = UserFacingErrorLocalizer.Localize(ex);
    }

    private void ClearModelError()
    {
        _modelErrorException = null;
        ModelHasError = false;
        ModelErrorText = "";
    }

    private void SetCharacterError(Exception ex)
    {
        _characterErrorException = ex;
        CharacterHasError = true;
        CharacterErrorText = UserFacingErrorLocalizer.Localize(ex);
    }

    private void ClearCharacterError()
    {
        _characterErrorException = null;
        CharacterHasError = false;
        CharacterErrorText = "";
    }

    public Microsoft.UI.Xaml.Visibility VoicevoxPoweredByNoteVisibility =>
        _loc.Current == AppLanguage.Japanese || string.IsNullOrWhiteSpace(VoicevoxPoweredByNoteText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    private void InitializeLocalization()
    {
        _loc.Changed += (_, _) => OnLocalizationChanged();
        ApplyLocalizedUi();
    }

    private void OnLocalizationChanged()
    {
        ApplyLocalizedUi();
        UpdateGeneralPreview();
        RefreshCharacterPresetLabels();
        RefreshRagSourceLabels();
        RefreshVoicevoxSpeakerLabels();
        RefreshLocalizedStatusTexts();
        RefreshMmprojDownloadForLanguage();
        if (SelectedChatModel is not null)
            UpdateMmprojStatus(SelectedChatModel.FileName);
        _ = RefreshRuntimeHealthAsync();
    }

    private void ApplyLocalizedUi()
    {
        UiErrorTitle = _loc.Get("Common.Error");
        UiTabGeneral = _loc.Get("Settings.Tab.General");
        UiTabModel = _loc.Get("Settings.Tab.Model");
        UiTabCharacter = _loc.Get("Settings.Tab.Character");
        UiTabRag = _loc.Get("Settings.Tab.Rag");
        UiTabVoicevox = _loc.Get("Settings.Tab.Voicevox");
        UiGeneralFontFamily = _loc.Get("Settings.General.FontFamily");
        UiGeneralUserDisplayName = _loc.Get("Settings.General.UserDisplayName");
        UiGeneralUserDisplayNameHint = _loc.Get("Settings.General.UserDisplayName.Hint");
        UiGeneralConfirmHistoryDelete = _loc.Get("Settings.General.ConfirmHistoryDelete");
        UiGeneralConfirmHistoryDeleteHint = _loc.Get("Settings.General.ConfirmHistoryDelete.Hint");
        UiGeneralPreview = _loc.Get("Settings.General.Preview");
        UiGeneralSave = _loc.Get("Common.Save");
        UiGeneralReset = _loc.Get("Settings.General.Reset");
        UiModelRuntimeStatus = _loc.Get("Settings.Model.RuntimeStatus");
        UiModelChatGguf = _loc.Get("Settings.Model.ChatGguf");
        UiModelMmprojHint = _loc.Get("Settings.Model.MmprojHint");
        UiModelApply = _loc.Get("Settings.Model.Apply");
        UiModelAdditionalFolder = _loc.Get("Settings.Model.AdditionalFolder");
        UiModelAdditionalFolderBrowse = _loc.Get("Settings.Model.AdditionalFolder.Browse");
        UiModelAdditionalFolderClear = _loc.Get("Settings.Model.AdditionalFolder.Clear");
        UiModelAdditionalFolderHint = _loc.Get("Settings.Model.AdditionalFolder.Hint");
        OnPropertyChanged(nameof(AdditionalModelsFolderDisplayText));
        UiCharacterRegistered = _loc.Get("Settings.Character.Registered");
        UiCharacterName = _loc.Get("Settings.Character.Name");
        UiCharacterPersona = _loc.Get("Settings.Character.Persona");
        UiApplyCharacterDefaults = _loc.Get("Settings.Character.ApplyDefaults");
        UiCharacterTemperatureHint = _loc.Get("Settings.Character.Hint.Temperature");
        UiCharacterTopPHint = _loc.Get("Settings.Character.Hint.TopP");
        UiCharacterTopKHint = _loc.Get("Settings.Character.Hint.TopK");
        UiCharacterContextLengthHint = _loc.Get("Settings.Character.Hint.ContextLength");
        UiCharacterMaxOutputTokensHint = _loc.Get("Settings.Character.Hint.MaxOutputTokens");
        UiSave = _loc.Get("Common.Save");
        UiDelete = _loc.Get("Common.Delete");
        UiOn = _loc.Get("Common.On");
        UiOff = _loc.Get("Common.Off");
        UiRagDescription = _loc.Get("Settings.Rag.Description");
        UiRagSourceUseHint = _loc.Get("Settings.Rag.SourceUseHint");
        UiRagChunkCountPrefix = _loc.Get("Settings.Rag.ChunkCount");
        UiRagIngestFile = _loc.Get("Settings.Rag.IngestFile");
        UiRagIngestFolder = _loc.Get("Settings.Rag.IngestFolder");
        UiVoicevoxEnabled = _loc.Get("Settings.Voicevox.Enabled");
        UiVoicevoxAutoSpeak = _loc.Get("Settings.Voicevox.AutoSpeak");
        UiVoicevoxSpeakInJapanesePronunciation = _loc.Get("Settings.Voicevox.SpeakInJapanesePronunciation");
        UiVoicevoxSpeakInJapanesePronunciationHint = _loc.Get("Settings.Voicevox.SpeakInJapanesePronunciation.Hint");
        UiVoicevoxSpeaker = _loc.Get("Settings.Voicevox.Speaker");
        UiVoicevoxSpeakerPlaceholder = _loc.Get("Settings.Voicevox.SpeakerPlaceholder");
        UiVoicevoxSave = _loc.Get("Settings.Voicevox.Save");
        ApplyLocalizedAboutUi();
        RefreshSliderLabels();
        UpdateGeneralPreview();
        RefreshVoicevoxPoweredByNote();
    }

    private void RefreshVoicevoxPoweredByNote()
    {
        if (_loc.Current == AppLanguage.Japanese)
            VoicevoxPoweredByNoteText = "";
        else
            VoicevoxPoweredByNoteText = _loc.Format("Settings.Voicevox.NonJaNotice", UiVoicevoxEnabled);

        OnPropertyChanged(nameof(VoicevoxPoweredByNoteVisibility));
    }

    private void RefreshCharacterPresetLabels()
    {
        if (CharacterPresets.Count == 0)
            return;

        var first = CharacterPresets[0];
        if (CharacterPresetService.IsNoneSelection(first.FileName))
            CharacterPresets[0] = new CharacterChoiceViewModel(first.FileName, _loc.Get("Character.Default"));
    }

    private void RefreshRagSourceLabels()
    {
        foreach (var row in RagSources)
            row.RefreshLocalization(_loc);
    }

    private void RefreshVoicevoxSpeakerLabels()
    {
        if (VoicevoxSpeakers.Count == 0)
            return;

        var selectedId = SelectedVoicevoxSpeaker?.Id;
        var rebuilt = VoicevoxSpeakers
            .Select(s => new VoicevoxSpeakerChoiceViewModel(s.Id, s.SpeakerName, s.StyleName))
            .ToList();

        VoicevoxSpeakers.Clear();
        foreach (var item in rebuilt)
            VoicevoxSpeakers.Add(item);

        if (selectedId is not null)
            SelectedVoicevoxSpeaker = VoicevoxSpeakers.FirstOrDefault(s => s.Id == selectedId);
    }
}
