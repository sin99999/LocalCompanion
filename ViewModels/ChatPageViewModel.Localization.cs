using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Localization;

namespace LocalCompanion.ViewModels;

public partial class ChatPageViewModel
{
    private readonly LocalizationService _loc = LocalizationService.Instance;

    [ObservableProperty] public partial string UiErrorTitle { get; set; }
    [ObservableProperty] public partial string UiRag { get; set; }
    [ObservableProperty] public partial string UiHistory { get; set; }
    [ObservableProperty] public partial string UiReasoning { get; set; }
    [ObservableProperty] public partial string UiClearHistory { get; set; }
    [ObservableProperty] public partial string UiInputPlaceholder { get; set; }
    [ObservableProperty] public partial string UiChatOptions { get; set; }
    [ObservableProperty] public partial string UiInsertMenu { get; set; }
    [ObservableProperty] public partial string UiInsertImage { get; set; }
    [ObservableProperty] public partial string UiInsertText { get; set; }
    [ObservableProperty] public partial string UiInsertUrl { get; set; }
    [ObservableProperty] public partial string UiRemoveAttachment { get; set; }

    private string? _statusKey;
    private object[]? _statusArgs;
    private bool _settingStatusByKey;

    /// <summary>ステータスをキーで設定する（言語切替時に再翻訳できる）。</summary>
    private void SetStatusByKey(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args.Length > 0 ? args : null;
        _settingStatusByKey = true;
        StatusText = args.Length > 0 ? _loc.Format(key, args) : _loc.Get(key);
        _settingStatusByKey = false;
    }

    partial void OnStatusTextChanged(string value)
    {
        // キー経由以外の直接代入は翻訳元キー不明として扱う
        if (!_settingStatusByKey)
            _statusKey = null;
    }

    private void InitializeLocalization()
    {
        _loc.Changed += (_, _) => OnLocalizationChanged();
        ApplyLocalizedUi();
        SetStatusByKey("Chat.Status.Ready");
        HealthText = _loc.Get("Chat.Status.HealthChecking");
    }

    private void OnLocalizationChanged()
    {
        ApplyLocalizedUi();
        ReloadCharacterChoices();
        foreach (var line in Messages)
            line.RefreshLocalization();

        RefreshWelcomeMessage();

        if (_lastErrorException is not null)
            ErrorText = UserFacingErrorLocalizer.Localize(_lastErrorException);

        if (_statusKey is not null)
            SetStatusByKey(_statusKey, _statusArgs ?? Array.Empty<object>());
        _ = RefreshHealthAsync();
    }

    private void ApplyLocalizedUi()
    {
        UiErrorTitle = _loc.Get("Chat.ErrorTitle");
        UiRag = _loc.Get("Chat.Rag");
        UiHistory = _loc.Get("Chat.History");
        UiReasoning = _loc.Get("Chat.Reasoning");
        UiClearHistory = _loc.Get("Chat.ClearHistory");
        UiInputPlaceholder = _loc.Get("Chat.InputPlaceholder");
        UiChatOptions = _loc.Get("Chat.Options");
        UiInsertMenu = _loc.Get("Chat.InsertMenu");
        UiInsertImage = _loc.Get("Chat.InsertImage");
        UiInsertText = _loc.Get("Chat.InsertText");
        UiInsertUrl = _loc.Get("Chat.InsertUrl");
        UiRemoveAttachment = _loc.Get("Chat.RemoveAttachment");
        NotifySendStopButtonLabelChanged();
    }
}
