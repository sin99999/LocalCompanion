using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Localization;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class ChatPageViewModel : ObservableObject
{
    private const int MaxInputHistory = 100;

    private readonly ChatService _chat;
    private readonly CharacterPresetService _characters;
    private readonly RuntimeHealthService _health;
    private readonly VoicevoxSpeechService _voicevoxSpeech;
    private readonly AppAppearanceService _appearance;
    private readonly List<string> _inputHistory = new();

    private int _inputHistoryIndex = -1;
    private string _inputHistoryDraft = string.Empty;
    private string? _activeSessionId;
    private bool _continueSession;
    private bool _suppressCharacterChangeReset;
    private string? _syncedCharacterFileName;
    private CancellationTokenSource? _sendCts;
    private Exception? _lastErrorException;

    public bool ImageAttachEnabled { get; private set; } = true;

    public string? ImageAttachHint { get; private set; }

    public ChatPageViewModel(
        ChatService chat,
        CharacterPresetService characters,
        RuntimeHealthService health,
        VoicevoxSpeechService voicevoxSpeech,
        AppAppearanceService appearance)
    {
        _chat = chat;
        _characters = characters;
        _health = health;
        _voicevoxSpeech = voicevoxSpeech;
        _appearance = appearance;
        InitializeLocalization();
        ReloadCharacterChoices();
        RefreshWelcomeMessage();
    }

    public ObservableCollection<ChatLineViewModel> Messages { get; } = new();

    public ObservableCollection<CharacterChoiceViewModel> CharacterChoices { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopGenerationCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HealthText { get; set; } = "";

    [ObservableProperty]
    public partial bool UseRag { get; set; } = true;

    [ObservableProperty]
    public partial bool UseHistory { get; set; } = true;

    [ObservableProperty]
    public partial bool UseReasoning { get; set; } = true;

    [ObservableProperty]
    public partial CharacterChoiceViewModel? SelectedCharacter { get; set; }

    public async Task RefreshHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var h = await _health.GetAsync(ct);
            HealthText = h.Message;
            ImageAttachEnabled = h.ImageAttachEnabled;
            ImageAttachHint = h.ImageAttachHint;
            OnPropertyChanged(nameof(ImageAttachEnabled));
            OnPropertyChanged(nameof(ImageAttachHint));
            if (!ImageAttachEnabled)
                ClearPendingImageAttachments();
        }
        catch (Exception ex)
        {
            HealthText = UserFacingErrorLocalizer.Localize(ex);
        }
    }

    public void ReloadCharacterChoices()
    {
        var list = _characters.List();
        var active = list.ActiveFileName ?? CharacterPresetService.NoneSelection;
        var characterChanged = !string.Equals(_syncedCharacterFileName, active, StringComparison.OrdinalIgnoreCase);

        CharacterChoices.Clear();
        CharacterChoices.Add(new CharacterChoiceViewModel(
            CharacterPresetService.NoneSelection,
            LocalizationService.Instance.Get("Character.Default")));
        foreach (var p in list.Presets)
            CharacterChoices.Add(new CharacterChoiceViewModel(p.FileName, p.Name));

        var match = CharacterChoices.FirstOrDefault(c =>
            string.Equals(c.FileName, active, StringComparison.OrdinalIgnoreCase))
            ?? CharacterChoices.FirstOrDefault();

        _suppressCharacterChangeReset = true;
        try
        {
            SelectedCharacter = match;
        }
        finally
        {
            _suppressCharacterChangeReset = false;
        }

        if (characterChanged)
        {
            _syncedCharacterFileName = active;
            BeginNewConversation();
        }
        else
        {
            RefreshWelcomeMessage();
        }
    }

    partial void OnSelectedCharacterChanged(CharacterChoiceViewModel? value)
    {
        if (value is null || IsBusy)
            return;

        var newKey = string.IsNullOrEmpty(value.FileName) || value.FileName == CharacterPresetService.NoneSelection
            ? CharacterPresetService.NoneSelection
            : value.FileName;
        var characterChanged = !string.Equals(_syncedCharacterFileName, newKey, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (newKey == CharacterPresetService.NoneSelection)
                _characters.SelectNone();
            else
                _characters.Select(newKey);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }

        if (!characterChanged)
            return;

        _syncedCharacterFileName = newKey;
        if (!_suppressCharacterChangeReset)
            BeginNewConversation();
    }

    public void BeginNewConversation()
    {
        DeleteActiveDefaultAiSessionIfAny();
        _activeSessionId = null;
        _continueSession = false;
        Messages.Clear();
        RefreshWelcomeMessage();
    }

    private void DeleteActiveDefaultAiSessionIfAny()
    {
        if (string.IsNullOrWhiteSpace(_activeSessionId))
            return;

        var session = _chat.GetSession(_activeSessionId);
        if (session is null || !CharacterPresetService.IsDefaultAiSession(session.PresetKey))
            return;

        _chat.DeleteSession(_activeSessionId);
    }

    public async Task FinalizeActiveSessionOnCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(_activeSessionId))
            return;
        if (!Messages.Any(m => !m.IsWelcomePlaceholder))
            return;

        try
        {
            var session = _chat.GetSession(_activeSessionId);
            if (session is not null && CharacterPresetService.IsDefaultAiSession(session.PresetKey))
                _chat.DeleteSession(_activeSessionId);
            else
                await _chat.FinalizeSessionAsync(_activeSessionId, CancellationToken.None);
        }
        catch
        {
            /* 終了時の要約失敗は無視 */
        }
        finally
        {
            _activeSessionId = null;
            _continueSession = false;
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        var historyDeleted = false;
        if (!string.IsNullOrWhiteSpace(_activeSessionId))
        {
            _chat.DeleteSession(_activeSessionId);
            historyDeleted = true;
        }

        BeginNewConversation();
        if (historyDeleted)
            SetStatusByKey("Chat.Status.HistoryCleared", 1);
        else
            SetStatusByKey("Chat.Status.NewConversation");
        HasError = false;
        ErrorText = string.Empty;
        ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ConversationThreadsChanged;

    public void LoadConversationSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var session = _chat.GetSession(sessionId);
        if (session is null)
            return;

        _suppressCharacterChangeReset = true;
        try
        {
            Messages.Clear();
            var assistantLabel = GetAssistantDisplayName(session.PresetKey);
            foreach (var (role, content) in _chat.LoadSessionMessages(sessionId))
            {
                var line = new ChatLineViewModel(role, content, role == "assistant" ? assistantLabel : null);
                if (role == "user")
                    ApplyUserDisplayName(line);
                Messages.Add(line);
            }

            var match = CharacterChoices.FirstOrDefault(c =>
                string.Equals(c.FileName, session.PresetKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                SelectedCharacter = match;
            else if (CharacterPresetService.IsDefaultAiSession(session.PresetKey))
                SelectedCharacter = CharacterChoices.First(c =>
                    c.FileName == CharacterPresetService.NoneSelection);
            else
            {
                try { _characters.Select(session.PresetKey); } catch { /* ignore */ }
            }

            _activeSessionId = sessionId;
            _continueSession = true;
        }
        finally
        {
            _suppressCharacterChangeReset = false;
        }

        SetStatusByKey(Messages.Count > 0 ? "Chat.Status.ThreadLoaded" : "Chat.Status.ThreadEmpty");
        HasError = false;
        ErrorText = string.Empty;
    }

    public bool RecallPreviousInput()
    {
        if (_inputHistory.Count == 0)
            return false;

        if (_inputHistoryIndex == -1)
        {
            _inputHistoryDraft = InputText;
            _inputHistoryIndex = 0;
        }
        else if (_inputHistoryIndex < _inputHistory.Count - 1)
        {
            _inputHistoryIndex++;
        }

        InputText = _inputHistory[_inputHistory.Count - 1 - _inputHistoryIndex];
        return true;
    }

    public bool RecallNextInput()
    {
        if (_inputHistory.Count == 0 || _inputHistoryIndex == -1)
            return false;

        if (_inputHistoryIndex <= 0)
        {
            _inputHistoryIndex = -1;
            InputText = _inputHistoryDraft;
            _inputHistoryDraft = string.Empty;
        }
        else
        {
            _inputHistoryIndex--;
            InputText = _inputHistory[_inputHistory.Count - 1 - _inputHistoryIndex];
        }

        return true;
    }

    private void PushInputHistory(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;
        if (_inputHistory.Count > 0 && _inputHistory[^1] == trimmed)
            return;

        _inputHistory.Add(trimmed);
        if (_inputHistory.Count > MaxInputHistory)
            _inputHistory.RemoveAt(0);
    }

    private void ResetInputHistoryNavigation()
    {
        _inputHistoryIndex = -1;
        _inputHistoryDraft = string.Empty;
    }

    private string GetAssistantDisplayName(string? presetKey = null)
    {
        var key = presetKey ?? _characters.GetActivePresetFileName();
        if (CharacterPresetService.IsNoneSelection(key))
            return LocalizationService.Instance.Get("Chat.Assistant.DefaultName");

        var preset = _characters.List().Presets.FirstOrDefault(p =>
            string.Equals(p.FileName, key, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(preset?.Name)
            ? LocalizationService.Instance.Get("Chat.Assistant.DefaultName")
            : preset.Name;
    }

    private void RefreshWelcomeMessage()
    {
        var welcome = Messages.FirstOrDefault(m => m.IsWelcomePlaceholder);
        var isDefault = CharacterPresetService.IsNoneSelection(_characters.GetActivePresetFileName());
        var hasConversation = Messages.Any(m => !m.IsWelcomePlaceholder);

        if (!isDefault || hasConversation)
        {
            if (welcome is not null)
                Messages.Remove(welcome);
            return;
        }

        var loc = LocalizationService.Instance;
        var greeting = loc.Get("Chat.Welcome.Default");
        var label = loc.Get("Chat.Assistant.DefaultName");

        if (welcome is not null)
        {
            welcome.SetText(greeting);
            welcome.RefreshLocalization();
            return;
        }

        Messages.Add(new ChatLineViewModel("assistant", greeting, label, isWelcomePlaceholder: true));
    }

    private void RemoveWelcomeIfPresent()
    {
        var welcome = Messages.FirstOrDefault(m => m.IsWelcomePlaceholder);
        if (welcome is not null)
            Messages.Remove(welcome);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var message = InputText.Trim();
        if (message.Length == 0 && PendingAttachments.Count == 0)
            return;

        HasError = false;
        ErrorText = string.Empty;
        _lastErrorException = null;
        RemoveWelcomeIfPresent();
        _voicevoxSpeech.Cancel();
        if (!string.IsNullOrWhiteSpace(message))
        {
            PushInputHistory(message);
            ResetInputHistoryNavigation();
        }

        var displayMessage = BuildUserDisplayMessage(message);
        var userLine = new ChatLineViewModel("user", displayMessage);
        ApplyUserDisplayName(userLine);
        Messages.Add(userLine);
        InputText = string.Empty;

        var (imagesBase64, attachedText, attachedFileName) = TakePendingAttachmentsForRequest();
        ClearPendingAttachments();

        // ユーザー発言を1フレーム描画してから生成開始
        await Task.Yield();

        IsBusy = true;
        NotifySendStopButtonLabelChanged();
        SetStatusByKey("Chat.Status.Generating");
        SendCommand.NotifyCanExecuteChanged();
        StopGenerationCommand.NotifyCanExecuteChanged();

        var sessionId = EnsureActiveSession();
        var req = new ChatRequestDto(
            message,
            ImagesBase64: imagesBase64,
            AttachedText: attachedText,
            AttachedFileName: attachedFileName,
            UseRag: UseRag,
            UseReasoning: UseReasoning,
            UseHistory: UseHistory,
            SessionId: sessionId,
            ContinueSession: _continueSession);
        ChatLineViewModel? assistantLine = null;
        string? replyText = null;
        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();
        var sendCt = _sendCts.Token;

        try
        {
            var replyAcc = new StringBuilder();
            var reasoningAcc = new StringBuilder();

            ChatLineViewModel EnsureAssistantLine()
            {
                if (assistantLine is not null)
                    return assistantLine;

                assistantLine = new ChatLineViewModel("assistant", "", GetAssistantDisplayName());
                Messages.Add(assistantLine);
                return assistantLine;
            }

            await foreach (var chunk in _chat.StreamChatAsync(req, sendCt))
            {
                switch (chunk.Type)
                {
                    case "content":
                    {
                        var line = EnsureAssistantLine();
                        if (replyAcc.Length == 0)
                            line.ClearReasoning();
                        replyAcc.Append(chunk.Text);
                        break;
                    }
                    case "reasoning" when UseReasoning:
                    {
                        var line = EnsureAssistantLine();
                        if (replyAcc.Length == 0)
                        {
                            reasoningAcc.Append(chunk.Text);
                            line.SetReasoning(reasoningAcc.ToString());
                        }
                        break;
                    }
                    case "done":
                        if (!string.IsNullOrWhiteSpace(chunk.Text))
                        {
                            EnsureAssistantLine();
                            replyAcc.Clear();
                            replyAcc.Append(chunk.Text);
                        }
                        break;
                }

                if (assistantLine is null)
                    continue;

                if (replyAcc.Length > 0)
                    assistantLine.ClearReasoning();
                if (replyAcc.Length > 0 || !string.IsNullOrWhiteSpace(assistantLine.ReasoningText))
                    assistantLine.SetText(replyAcc.ToString());
            }

            if (replyAcc.Length == 0)
                throw new InvalidOperationException(LocalizationService.Instance.Get("Chat.Status.EmptyReply"));

            replyText = replyAcc.ToString();
            var isDefaultAi = CharacterPresetService.IsNoneSelection(_characters.GetActivePresetFileName());
            SetStatusByKey(!string.IsNullOrWhiteSpace(sessionId)
                ? isDefaultAi
                    ? "Chat.Status.SessionContinued"
                    : "Chat.Status.SessionSaved"
                : "Chat.Status.Done");

            if (!string.IsNullOrWhiteSpace(replyText))
                _ = _voicevoxSpeech.MaybeSpeakAssistantAsync(replyText);

            if (!string.IsNullOrWhiteSpace(sessionId))
                _continueSession = true;

            ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
            if (!isDefaultAi)
                MainWindow.Instance?.EnsureConversationHistoryVisible();
            _ = RefreshHealthAsync();
        }
        catch (OperationCanceledException) when (sendCt.IsCancellationRequested)
        {
            var loc = LocalizationService.Instance;
            SetStatusByKey("Chat.Status.Stopped");
            if (assistantLine is null)
            {
                assistantLine = new ChatLineViewModel("assistant", loc.Get("Chat.Status.StoppedHint"), GetAssistantDisplayName());
                Messages.Add(assistantLine);
            }
            else if (string.IsNullOrWhiteSpace(assistantLine.Text))
            {
                assistantLine.SetText(loc.Get("Chat.Status.StoppedHint"));
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            SetStatusByKey("Chat.Status.Error");
            if (assistantLine is null)
                Messages.Add(new ChatLineViewModel("system", ErrorText));
            else if (string.IsNullOrEmpty(assistantLine.Text))
                assistantLine.SetText(ErrorText);
            else
                Messages.Add(new ChatLineViewModel("system", ErrorText));
        }
        finally
        {
            IsBusy = false;
            NotifySendStopButtonLabelChanged();
            SendCommand.NotifyCanExecuteChanged();
            StopGenerationCommand.NotifyCanExecuteChanged();
            if (_sendCts is not null)
            {
                _sendCts.Dispose();
                _sendCts = null;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopGeneration))]
    private void StopGeneration()
    {
        _sendCts?.Cancel();
        _voicevoxSpeech.Cancel();
    }

    public string SendStopButtonLabel =>
        IsBusy
            ? LocalizationService.Instance.Get("Chat.Stop")
            : LocalizationService.Instance.Get("Chat.Send");

    public bool IsInputEnabled => !IsBusy;

    private void NotifySendStopButtonLabelChanged() =>
        OnPropertyChanged(nameof(SendStopButtonLabel));

    partial void OnIsBusyChanged(bool value)
    {
        NotifySendStopButtonLabelChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
    }

    private bool CanSend() =>
        !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || PendingAttachments.Count > 0);

    private bool CanStopGeneration() => IsBusy;

    private string? EnsureActiveSession()
    {
        if (!string.IsNullOrWhiteSpace(_activeSessionId))
            return _activeSessionId;

        var sessionKey = CharacterPresetService.ResolveSessionPresetKey(_characters.GetActivePresetFileName());
        _activeSessionId = _chat.CreateSession(sessionKey);
        return _activeSessionId;
    }

    public void RefreshUserMessageHeaders()
    {
        foreach (var line in Messages)
        {
            if (line.Role == "user")
                ApplyUserDisplayName(line);
        }
    }

    private void ApplyUserDisplayName(ChatLineViewModel line)
    {
        var name = _appearance.Current.UserDisplayName?.Trim();
        line.SetUserLabel(string.IsNullOrEmpty(name) ? null : name);
    }

    private void SetError(Exception ex)
    {
        _lastErrorException = ex;
        HasError = true;
        ErrorText = UserFacingErrorLocalizer.Localize(ex);
    }

    public void ReportError(Exception ex) => SetError(ex);
}

public sealed record CharacterChoiceViewModel(string FileName, string DisplayName);
