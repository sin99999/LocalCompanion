using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private readonly ModelCatalogService _models;
    private readonly CharacterPresetService _characters;
    private readonly CharacterRepository _characterRepository;
    private readonly LlamaLifecycleService _lifecycle;
    private readonly RagService _rag;
    private readonly RagDatabase _ragDb;
    private readonly AppPaths _paths;
    private readonly VoicevoxClient _voicevoxClient;
    private readonly VoicevoxLifecycleService _voicevoxLifecycle;
    private readonly VoicevoxInstallLocator _voicevoxLocator;
    private readonly VoicevoxSettingsStore _voicevoxSettings;
    private readonly VoicevoxSpeakerCacheStore _speakerCache;
    private readonly AppAppearanceService _appearance;
    private readonly RuntimeHealthService _health;
    private int _voicevoxLoadGeneration;

    public SettingsPageViewModel(
        AppPaths paths,
        ModelCatalogService models,
        CharacterPresetService characters,
        CharacterRepository characterRepository,
        LlamaLifecycleService lifecycle,
        RagService rag,
        RagDatabase ragDb,
        VoicevoxClient voicevoxClient,
        VoicevoxLifecycleService voicevoxLifecycle,
        VoicevoxInstallLocator voicevoxLocator,
        VoicevoxSettingsStore voicevoxSettings,
        VoicevoxSpeakerCacheStore speakerCache,
        AppAppearanceService appearance,
        RuntimeHealthService health)
    {
        _paths = paths;
        _models = models;
        _characters = characters;
        _characterRepository = characterRepository;
        _lifecycle = lifecycle;
        _rag = rag;
        _ragDb = ragDb;
        _voicevoxClient = voicevoxClient;
        _voicevoxLifecycle = voicevoxLifecycle;
        _voicevoxLocator = voicevoxLocator;
        _voicevoxSettings = voicevoxSettings;
        _speakerCache = speakerCache;
        _appearance = appearance;
        _health = health;
        InitializeLocalization();
        RefreshSliderLabels();
        RefreshChatFontChoices();
    }

    public ObservableCollection<VoicevoxSpeakerChoiceViewModel> VoicevoxSpeakers { get; } = new();
    public ObservableCollection<GgufFileInfo> ChatModels { get; } = new();
    public ObservableCollection<CharacterChoiceViewModel> CharacterPresets { get; } = new();
    public ObservableCollection<RagSourceRowViewModel> RagSources { get; } = new();

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = "";

    [ObservableProperty]
    public partial string AdditionalModelsFolder { get; set; } = "";

    public bool HasAdditionalModelsFolder => !string.IsNullOrWhiteSpace(AdditionalModelsFolder);

    public Microsoft.UI.Xaml.Visibility AdditionalModelsFolderClearVisibility =>
        HasAdditionalModelsFolder ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnAdditionalModelsFolderChanged(string value)
    {
        OnPropertyChanged(nameof(HasAdditionalModelsFolder));
        OnPropertyChanged(nameof(AdditionalModelsFolderDisplayText));
        OnPropertyChanged(nameof(AdditionalModelsFolderClearVisibility));
    }

    public string AdditionalModelsFolderDisplayText =>
        HasAdditionalModelsFolder
            ? AdditionalModelsFolder
            : LocalizationService.Instance.Get("Settings.Model.AdditionalFolder.None");

    /// <summary>追加モデルフォルダを設定（null/空で解除）。設定のみ保存し、フォルダには書き込まない。</summary>
    public void SetAdditionalModelsFolder(string? folder)
    {
        _models.SetAdditionalModelsFolder(folder);
        Refresh();
    }

    [ObservableProperty]
    public partial string MmprojStatusText { get; set; } = "";

    [ObservableProperty]
    public partial string CharactersDirectory { get; set; } = "";

    [ObservableProperty]
    public partial string RagDataDirectory { get; set; } = "";

    [ObservableProperty]
    public partial int RagChunkCount { get; set; }

    [ObservableProperty]
    public partial GgufFileInfo? SelectedChatModel { get; set; }

    [ObservableProperty]
    public partial CharacterChoiceViewModel? SelectedCharacterPreset { get; set; }

    [ObservableProperty]
    public partial string CharacterName { get; set; } = "";

    [ObservableProperty]
    public partial string CharacterPersona { get; set; } = "";

    [ObservableProperty]
    public partial string CharacterSpeakingStyle { get; set; } = "";

    [ObservableProperty]
    public partial double CharacterTemperature { get; set; } = CharacterDefaults.AppTemperature;

    [ObservableProperty]
    public partial double CharacterTopP { get; set; } = CharacterDefaults.AppTopP;

    [ObservableProperty]
    public partial double CharacterTopK { get; set; } = CharacterDefaults.AppTopK;

    [ObservableProperty]
    public partial double CharacterContextLength { get; set; } = CharacterDefaults.AppContextLength;

    [ObservableProperty]
    public partial double CharacterMaxOutputTokens { get; set; } = CharacterDefaults.AppMaxOutputTokens;

    [ObservableProperty]
    public partial bool IsVoicevoxInstalled { get; set; }

    [ObservableProperty]
    public partial string VoicevoxSpeakersStatus { get; set; } = "";

    [ObservableProperty]
    public partial string VoicevoxPoweredByText { get; set; } = "Powered by VOICEVOX";

    public Microsoft.UI.Xaml.Visibility VoicevoxSpeakersStatusVisibility =>
        string.IsNullOrWhiteSpace(VoicevoxSpeakersStatus)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    [ObservableProperty]
    public partial bool VoicevoxEnabled { get; set; }

    [ObservableProperty]
    public partial bool VoicevoxAutoSpeak { get; set; } = true;

    [ObservableProperty]
    public partial bool VoicevoxSpeakInJapanesePronunciation { get; set; }

    [ObservableProperty]
    public partial VoicevoxSpeakerChoiceViewModel? SelectedVoicevoxSpeaker { get; set; }

    [ObservableProperty]
    public partial double VoicevoxSpeedScale { get; set; } = 1.0;

    [ObservableProperty]
    public partial double VoicevoxPitchScale { get; set; }

    [ObservableProperty]
    public partial double VoicevoxIntonationScale { get; set; } = 1.0;

    [ObservableProperty]
    public partial double VoicevoxVolumeScale { get; set; } = 1.0;

    [ObservableProperty]
    public partial string ModelStatusText { get; set; } = "";

    [ObservableProperty]
    public partial string CharacterStatusText { get; set; } = "";

    /// <summary>性格・指示入力欄の高さ（約3行）。保存メッセージ行は下段で常に確保する。</summary>
    public double CharacterPersonaBoxHeight => 72;

    [ObservableProperty]
    public partial string RagStatusText { get; set; } = "";

    [ObservableProperty]
    public partial string VoicevoxStatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsSettingsInputEnabled => !IsBusy && !IsModelLoadInProgress;

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(IsSettingsInputEnabled));

    [ObservableProperty]
    public partial bool ModelHasError { get; set; }

    [ObservableProperty]
    public partial string ModelErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool CharacterHasError { get; set; }

    [ObservableProperty]
    public partial string CharacterErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool RagHasError { get; set; }

    [ObservableProperty]
    public partial string RagErrorText { get; set; } = "";

    public string GeneralChatFontSizeLabel { get; private set; } = "";
    public string CharacterTemperatureLabel { get; private set; } = "";
    public string CharacterTopPLabel { get; private set; } = "";
    public string CharacterTopKLabel { get; private set; } = "";
    public string CharacterContextLengthLabel { get; private set; } = "";
    public string CharacterMaxOutputTokensLabel { get; private set; } = "";

    public double CharacterMaxOutputTokensSliderMaximum =>
        CharacterSamplingLimits.MaxOutputTokensCapForContext((int)Math.Round(CharacterContextLength));
    public string VoicevoxSpeedScaleLabel { get; private set; } = "";
    public string VoicevoxPitchScaleLabel { get; private set; } = "";
    public string VoicevoxIntonationScaleLabel { get; private set; } = "";
    public string VoicevoxVolumeScaleLabel { get; private set; } = "";

    public bool CanDeleteSelectedCharacter =>
        SelectedCharacterPreset is not null
        && !string.IsNullOrEmpty(SelectedCharacterPreset.FileName)
        && !CharacterPresetService.IsNoneSelection(SelectedCharacterPreset.FileName);

    public void Refresh()
    {
        LoadGeneralSettings();
        RefreshAboutInfo();
        var scan = _models.Scan();
        ModelsDirectory = scan.ModelsDirectory;
        AdditionalModelsFolder = scan.AdditionalModelsFolder ?? "";
        ChatModels.Clear();
        foreach (var m in scan.ChatModels)
            ChatModels.Add(m);

        SelectedChatModel = ChatModels.FirstOrDefault(m =>
                scan.Selection.ModelFullPath is not null
                && m.FullPath.Equals(scan.Selection.ModelFullPath, StringComparison.OrdinalIgnoreCase))
            ?? ChatModels.FirstOrDefault(m =>
                string.Equals(m.FileName, scan.Selection.ModelFileName, StringComparison.OrdinalIgnoreCase));
        UpdateMmprojStatus(SelectedChatModel?.FileName);

        var chars = _characters.List();
        CharactersDirectory = chars.CharactersDirectory;
        CharacterPresets.Clear();
        CharacterPresets.Add(new CharacterChoiceViewModel(
            CharacterPresetService.NoneSelection,
            LocalizationService.Instance.Get("Character.Default")));
        foreach (var p in chars.Presets)
            CharacterPresets.Add(new CharacterChoiceViewModel(p.FileName, p.Name));
        var active = chars.ActiveFileName ?? CharacterPresetService.NoneSelection;
        SelectedCharacterPreset = CharacterPresets.FirstOrDefault(c =>
            string.Equals(c.FileName, active, StringComparison.OrdinalIgnoreCase));
        LoadCharacterForm(SelectedCharacterPreset?.FileName);
        DeleteCharacterCommand.NotifyCanExecuteChanged();

        RagDataDirectory = _ragDb.DataDirectory;
        RagChunkCount = _rag.GetChunkCount();
        RagSources.Clear();
        foreach (var s in _rag.ListSources())
        {
            RagSources.Add(new RagSourceRowViewModel(
                s.Source,
                s.Chunks,
                s.FileExists,
                s.Enabled,
                (source, enabled) => _rag.SetSourceEnabled(source, enabled)));
        }

        IsVoicevoxInstalled = _voicevoxLocator.DescribeInstall().Installed;
        var vv = _voicevoxSettings.Load();
        VoicevoxEnabled = vv.Enabled;
        VoicevoxAutoSpeak = vv.AutoSpeak;
        VoicevoxSpeakInJapanesePronunciation = vv.SpeakInJapanesePronunciation;
        VoicevoxSpeedScale = vv.SpeedScale;
        VoicevoxPitchScale = vv.PitchScale;
        VoicevoxIntonationScale = vv.IntonationScale;
        VoicevoxVolumeScale = vv.VolumeScale;
        RefreshSliderLabels();
        _ = RefreshRuntimeHealthAsync();
    }

    public async Task LoadVoicevoxSpeakersAsync(CancellationToken ct = default)
    {
        var generation = Interlocked.Increment(ref _voicevoxLoadGeneration);
        SetVoicevoxSpeakersStatus("");
        if (!IsVoicevoxInstalled)
        {
            RunOnUi(() =>
            {
                VoicevoxSpeakers.Clear();
                SelectedVoicevoxSpeaker = null;
            });
            VoicevoxPoweredByText = "Powered by VOICEVOX";
            return;
        }

        try
        {
            var status = await _voicevoxLifecycle.EnsureRunningAsync(ct);
            if (generation != _voicevoxLoadGeneration)
                return;
            UpdateVoicevoxPoweredByText(status.Version);
            if (!status.Available)
            {
                RunOnUi(() =>
                {
                    VoicevoxSpeakers.Clear();
                    SelectedVoicevoxSpeaker = null;
                });
                SetVoicevoxSpeakersStatus(status.Hint ?? LocalizationService.Instance.Get("Settings.Voicevox.Waiting"));
                return;
            }

            var speakers = await _voicevoxClient.ListSpeakersAsync(ct);
            if (generation != _voicevoxLoadGeneration)
                return;
            RunOnUi(() =>
            {
                VoicevoxSpeakers.Clear();
                foreach (var s in speakers)
                    VoicevoxSpeakers.Add(new VoicevoxSpeakerChoiceViewModel(s.Id, s.SpeakerName, s.StyleName));

                var saved = _voicevoxSettings.Load();
                SelectVoicevoxSpeaker(saved);
            });
            _speakerCache.Save(speakers);

            if (VoicevoxSpeakers.Count == 0)
                SetVoicevoxSpeakersStatus(LocalizationService.Instance.Get("Settings.Voicevox.NoSpeakers"));
        }
        catch
        {
            RunOnUi(() =>
            {
                VoicevoxSpeakers.Clear();
                SelectedVoicevoxSpeaker = null;
            });
            SetVoicevoxSpeakersStatus(LocalizationService.Instance.Get("Settings.Voicevox.LoadFailed"));
        }
    }

    private void UpdateVoicevoxPoweredByText(string? version)
    {
        var normalized = VoicevoxUpdateService.NormalizeVersion(version);
        VoicevoxPoweredByText = string.IsNullOrWhiteSpace(normalized)
            ? "Powered by VOICEVOX"
            : $"Powered by VOICEVOX {normalized}";
    }

    private void SetVoicevoxSpeakersStatus(string text)
    {
        VoicevoxSpeakersStatus = text;
        OnPropertyChanged(nameof(VoicevoxSpeakersStatusVisibility));
    }

    private void SelectVoicevoxSpeaker(VoicevoxSettingsDto saved)
    {
        if (saved.SpeakerChosenByUser)
        {
            SelectedVoicevoxSpeaker = VoicevoxSpeakers.FirstOrDefault(s => s.Id == saved.SpeakerId)
                ?? VoicevoxSpeakers.FirstOrDefault();
            return;
        }

        SelectedVoicevoxSpeaker = VoicevoxSpeakers.FirstOrDefault();
        if (SelectedVoicevoxSpeaker is not null)
        {
            _voicevoxSettings.Save(new VoicevoxSettingsDto
            {
                Enabled = saved.Enabled,
                AutoSpeak = saved.AutoSpeak,
                SpeakInJapanesePronunciation = saved.SpeakInJapanesePronunciation,
                SpeakerId = SelectedVoicevoxSpeaker.Id,
                SpeakerChosenByUser = false,
                SpeedScale = saved.SpeedScale,
                PitchScale = saved.PitchScale,
                IntonationScale = saved.IntonationScale,
                VolumeScale = saved.VolumeScale,
                PrePhonemeLength = saved.PrePhonemeLength,
                PostPhonemeLength = saved.PostPhonemeLength,
            });
        }
    }

    partial void OnSelectedChatModelChanged(GgufFileInfo? value)
    {
        if (value is null)
        {
            MmprojStatusText = "";
            QueueMmprojEnsureForSelection(null);
            return;
        }

        QueueMmprojEnsureForSelection(value.FileName, value.FullPath);
    }

    private void UpdateMmprojStatus(string? modelFileName)
    {
        if (string.IsNullOrWhiteSpace(modelFileName))
        {
            MmprojStatusText = "";
            return;
        }

        var scan = _models.Scan();
        var loc = LocalizationService.Instance;
        MmprojStatusText = !string.IsNullOrWhiteSpace(scan.SuggestedMmproj)
            ? loc.Format("Settings.Model.MmprojFound", scan.SuggestedMmproj)
            : loc.Get("Settings.Model.MmprojMissing");
    }

    [RelayCommand]
    private async Task ApplyModelAsync()
    {
        if (SelectedChatModel is null)
        {
            ModelHasError = true;
            ModelErrorText = LocalizationService.Instance.Get("Settings.Model.SelectRequired");
            return;
        }

        IsBusy = true;
        ClearModelError();

        try
        {
            await WaitForMmprojDownloadAsync();
            if (MmprojSupport.NeedsKnownMmprojDownload(
                    _paths.Root,
                    SelectedChatModel.FileName,
                    SelectedChatModel.FullPath))
            {
                _mmprojDownloadCts?.Cancel();
                _mmprojDownloadCts?.Dispose();
                _mmprojDownloadCts = new CancellationTokenSource();
                await EnsureMmprojForModelAsync(
                    SelectedChatModel.FileName,
                    SelectedChatModel.FullPath,
                    _mmprojDownloadCts.Token,
                    ++_mmprojDownloadGeneration);
            }

            _models.Select(new SelectModelRequest(SelectedChatModel.FileName, null, SelectedChatModel.FullPath));

            var code = await RestartManagedWithProgressAsync(CancellationToken.None);
            if (code != 0)
                throw new InvalidOperationException(LocalizationService.Instance.Get("Startup.LlamaFailed"));

            SetModelStatus("Settings.Model.Saved");
            Refresh();
            await RefreshRuntimeHealthAsync();
        }
        catch (Exception ex)
        {
            SetModelError(ex);
            SetModelStatus("Common.Error");
            await RefreshRuntimeHealthAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedCharacterPresetChanged(CharacterChoiceViewModel? value)
    {
        LoadCharacterForm(value?.FileName);
        DeleteCharacterCommand.NotifyCanExecuteChanged();
    }

    private void LoadCharacterForm(string? fileName)
    {
        var profile = string.IsNullOrEmpty(fileName) || CharacterPresetService.IsNoneSelection(fileName)
            ? _characters.BareModelProfile()
            : _characters.GetByFileName(fileName) ?? _characters.BareModelProfile();

        CharacterName = CharacterPresetService.IsNoneSelection(fileName) ? "" : profile.Name;
        CharacterPersona = profile.Persona;
        CharacterSpeakingStyle = profile.SpeakingStyle;
        CharacterTemperature = profile.Temperature;
        CharacterTopP = profile.TopP;
        CharacterTopK = profile.TopK;
        CharacterContextLength = profile.ContextLength;
        CharacterMaxOutputTokens = profile.MaxOutputTokens;
        ClampCharacterMaxOutputTokens();
        RefreshSliderLabels();
    }

    [RelayCommand]
    private void ApplyCharacterDefaults()
    {
        CharacterTemperature = CharacterDefaults.Temperature;
        CharacterTopP = CharacterDefaults.TopP;
        CharacterTopK = CharacterDefaults.TopK;
        CharacterContextLength = CharacterDefaults.ContextLength;
        CharacterMaxOutputTokens = CharacterDefaults.MaxOutputTokens;
        ClampCharacterMaxOutputTokens();
        RefreshSliderLabels();
    }

    [RelayCommand]
    private void SaveCharacter()
    {
        if (string.IsNullOrWhiteSpace(CharacterName))
        {
            CharacterHasError = true;
            CharacterErrorText = LocalizationService.Instance.Get("Settings.Character.NameRequired");
            return;
        }

        CharacterHasError = false;
        CharacterErrorText = "";
        _characterErrorException = null;
        try
        {
            var profile = new CharacterProfileDto(
                CharacterName.Trim(),
                CharacterPersona.Trim(),
                CharacterSpeakingStyle.Trim(),
                CharacterTemperature,
                CharacterTopP,
                (int)Math.Round(CharacterTopK),
                (int)Math.Round(CharacterContextLength),
                (int)Math.Round(CharacterMaxOutputTokens));
            var fileName = _characterRepository.Save(profile);
            SetCharacterStatus("Settings.Character.Saved", fileName);
            Refresh();
        }
        catch (Exception ex)
        {
            SetCharacterError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCharacter))]
    private void DeleteCharacter()
    {
        if (!CanDeleteSelectedCharacter || SelectedCharacterPreset is null)
            return;

        CharacterHasError = false;
        CharacterErrorText = "";
        _characterErrorException = null;
        SetCharacterStatus(null);
        try
        {
            _characters.Delete(SelectedCharacterPreset.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            SetCharacterError(ex);
        }
    }

    [RelayCommand]
    private void SaveVoicevox()
    {
        var speakerId = SelectedVoicevoxSpeaker?.Id ?? VoicevoxSpeakers.FirstOrDefault()?.Id ?? 0;
        var saved = _voicevoxSettings.Save(new VoicevoxSettingsDto
        {
            Enabled = VoicevoxEnabled,
            AutoSpeak = VoicevoxAutoSpeak,
            SpeakInJapanesePronunciation = VoicevoxSpeakInJapanesePronunciation,
            SpeakerId = speakerId,
            SpeakerChosenByUser = true,
            SpeedScale = VoicevoxSpeedScale,
            PitchScale = VoicevoxPitchScale,
            IntonationScale = VoicevoxIntonationScale,
            VolumeScale = VoicevoxVolumeScale,
        });
        VoicevoxEnabled = saved.Enabled;
        VoicevoxAutoSpeak = saved.AutoSpeak;
        VoicevoxSpeakInJapanesePronunciation = saved.SpeakInJapanesePronunciation;
        VoicevoxSpeedScale = saved.SpeedScale;
        VoicevoxPitchScale = saved.PitchScale;
        VoicevoxIntonationScale = saved.IntonationScale;
        VoicevoxVolumeScale = saved.VolumeScale;
        SelectVoicevoxSpeaker(saved);
        SetVoicevoxStatus("Settings.Voicevox.Saved");
    }

    [RelayCommand]
    private void DeleteRagSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        ClearRagError();
        try
        {
            var n = _rag.DeleteSource(source);
            if (n > 0)
                SetRagStatus("Settings.Rag.SourceDeleted", Path.GetFileName(source));
            else
                SetRagStatus("Settings.Rag.SourceNotFound");
            Refresh();
        }
        catch (Exception ex)
        {
            SetRagError(ex);
        }
    }

    public async Task IngestPathAsync(string path, CancellationToken ct)
    {
        IsBusy = true;
        ClearRagError();
        SetRagStatus("Settings.Rag.Ingesting");
        try
        {
            var result = await _rag.IngestPathAsync(path, ct);
            SetRagStatus("Settings.Rag.IngestDone", result.Files, result.Chunks);
            Refresh();
        }
        catch (Exception ex)
        {
            SetRagError(ex);
            SetRagStatus("Settings.Rag.IngestFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCharacterTemperatureChanged(double value) => RefreshSliderLabels();
    partial void OnCharacterTopPChanged(double value) => RefreshSliderLabels();
    partial void OnCharacterTopKChanged(double value) => RefreshSliderLabels();
    partial void OnCharacterContextLengthChanged(double value)
    {
        ClampCharacterMaxOutputTokens();
        RefreshSliderLabels();
    }
    partial void OnCharacterMaxOutputTokensChanged(double value) => RefreshSliderLabels();
    partial void OnVoicevoxSpeedScaleChanged(double value) => RefreshSliderLabels();
    partial void OnVoicevoxPitchScaleChanged(double value) => RefreshSliderLabels();
    partial void OnVoicevoxIntonationScaleChanged(double value) => RefreshSliderLabels();
    partial void OnVoicevoxVolumeScaleChanged(double value) => RefreshSliderLabels();

    private void ClampCharacterMaxOutputTokens()
    {
        var cap = CharacterMaxOutputTokensSliderMaximum;
        if (CharacterMaxOutputTokens > cap)
            CharacterMaxOutputTokens = cap;
        OnPropertyChanged(nameof(CharacterMaxOutputTokensSliderMaximum));
    }

    private void RefreshSliderLabels()
    {
        var loc = LocalizationService.Instance;
        GeneralChatFontSizeLabel = loc.Format("Settings.Slider.FontSize", GeneralChatFontSize.ToString("0"));
        CharacterTemperatureLabel = loc.Format("Settings.Slider.Temperature", CharacterTemperature.ToString("0.##"));
        CharacterTopPLabel = loc.Format("Settings.Slider.TopP", CharacterTopP.ToString("0.##"));
        CharacterTopKLabel = loc.Format("Settings.Slider.TopK", Math.Round(CharacterTopK).ToString("0"));
        CharacterContextLengthLabel = loc.Format("Settings.Slider.ContextLength", Math.Round(CharacterContextLength).ToString("0"));
        CharacterMaxOutputTokensLabel = loc.Format("Settings.Slider.MaxOutputTokens", Math.Round(CharacterMaxOutputTokens).ToString("0"));
        VoicevoxSpeedScaleLabel = loc.Format("Settings.Voicevox.Slider.Speed", VoicevoxSpeedScale.ToString("0.##"));
        VoicevoxPitchScaleLabel = loc.Format("Settings.Voicevox.Slider.Pitch", VoicevoxPitchScale.ToString("0.##"));
        VoicevoxIntonationScaleLabel = loc.Format("Settings.Voicevox.Slider.Intonation", VoicevoxIntonationScale.ToString("0.##"));
        VoicevoxVolumeScaleLabel = loc.Format("Settings.Voicevox.Slider.Volume", VoicevoxVolumeScale.ToString("0.##"));

        OnPropertyChanged(nameof(GeneralChatFontSizeLabel));
        OnPropertyChanged(nameof(CharacterTemperatureLabel));
        OnPropertyChanged(nameof(CharacterTopPLabel));
        OnPropertyChanged(nameof(CharacterTopKLabel));
        OnPropertyChanged(nameof(CharacterContextLengthLabel));
        OnPropertyChanged(nameof(CharacterMaxOutputTokensLabel));
        OnPropertyChanged(nameof(CharacterMaxOutputTokensSliderMaximum));
        OnPropertyChanged(nameof(VoicevoxSpeedScaleLabel));
        OnPropertyChanged(nameof(VoicevoxPitchScaleLabel));
        OnPropertyChanged(nameof(VoicevoxIntonationScaleLabel));
        OnPropertyChanged(nameof(VoicevoxVolumeScaleLabel));
    }
}
