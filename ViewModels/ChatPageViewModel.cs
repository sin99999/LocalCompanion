using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class ChatPageViewModel : ObservableObject
{
    private const int MaxInputHistory = 100;

    private readonly ChatService _chat;
    private readonly RagService _rag;
    private readonly CharacterPresetService _characters;
    private readonly RuntimeHealthService _health;
    private readonly VoicevoxSpeechService _voicevoxSpeech;
    private readonly AppAppearanceService _appearance;
    private readonly SpeechInputService _speechInput;
    private readonly CharacterSelfImproveService _selfImprove;
    private readonly List<string> _inputHistory = new();

    private int _inputHistoryIndex = -1;
    private string _inputHistoryDraft = string.Empty;
    private string? _activeSessionId;
    private bool _continueSession;
    private bool _suppressCharacterChangeReset;
    private string? _syncedCharacterFileName;
    private CancellationTokenSource? _sendCts;
    private CancellationTokenSource? _selfImproveCts;
    private Exception? _lastErrorException;
    private readonly SemaphoreSlim _sessionFinalizeGate = new(1, 1);
    private int _sessionFinalizeGeneration;
    private int _selfImproveDialogGate;
    private int _selfImproveBusy;

    public bool ImageAttachEnabled { get; private set; } = true;

    public string? ImageAttachHint { get; private set; }

    public event EventHandler<CharacterSelfImproveProposal>? SelfImproveProposed;

    public ChatPageViewModel(
        ChatService chat,
        RagService rag,
        CharacterPresetService characters,
        RuntimeHealthService health,
        VoicevoxSpeechService voicevoxSpeech,
        AppAppearanceService appearance,
        SpeechInputService speechInput,
        CharacterSelfImproveService selfImprove)
    {
        _chat = chat;
        _rag = rag;
        _characters = characters;
        _health = health;
        _selfImprove = selfImprove;
        _voicevoxSpeech = voicevoxSpeech;
        _appearance = appearance;
        _speechInput = speechInput;
        _appearance.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsSpeechInputVisible));
            OnPropertyChanged(nameof(SpeechInputVisibility));
        };
        _speechInput.ListeningChanged += (_, _) =>
            RunOnUi(RefreshSpeechListeningUi);
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

    public bool IsSpeechInputVisible => _appearance.Current.SpeechInputEnabled;

    public Microsoft.UI.Xaml.Visibility SpeechInputVisibility =>
        IsSpeechInputVisible
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>音声認識中（再クリックでキャンセル可）。</summary>
    public bool IsSpeechListening => _speechInput.IsListening;

    /// <summary>マイクボタンの表示グリフ（認識中は停止アイコン）。</summary>
    public string SpeechInputGlyph => IsSpeechListening ? "\uE71A" : "\uE720";

    [ObservableProperty]
    public partial bool UseReasoning { get; set; } = true;

    [ObservableProperty]
    public partial CharacterChoiceViewModel? SelectedCharacter { get; set; }

    public async Task RefreshHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var h = await _health.GetAsync(ct);
            RunOnUi(() =>
            {
                HealthText = h.Message;
                ImageAttachEnabled = h.ImageAttachEnabled;
                ImageAttachHint = h.ImageAttachHint;
                OnPropertyChanged(nameof(ImageAttachEnabled));
                OnPropertyChanged(nameof(ImageAttachHint));
                if (!ImageAttachEnabled)
                    ClearPendingImageAttachments();
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() => HealthText = UserFacingErrorLocalizer.Localize(ex));
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
            _ = FinalizeAndBeginNewConversationAsync();
        }
        else
        {
            RefreshWelcomeMessage();
        }
    }

    partial void OnSelectedCharacterChanged(CharacterChoiceViewModel? value)
    {
        if (value is null || IsBusy || IsSelfImproveBusy)
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
            _ = FinalizeAndBeginNewConversationAsync();
    }

    public void BeginNewConversation() =>
        _ = FinalizeAndBeginNewConversationAsync();

    /// <summary>
    /// 現在セッションを閉じる前に長期記憶を抽出し、新規会話へ移る。
    /// </summary>
    public async Task FinalizeAndBeginNewConversationAsync()
    {
        if (IsBusy)
        {
            NotifyBusyMutationBlocked();
            return;
        }

        await _sessionFinalizeGate.WaitAsync();
        var generation = Interlocked.Increment(ref _sessionFinalizeGeneration);
        try
        {
            // 送信と競合しないよう、先にアクティブ ID を外してから終了処理する
            var sessionId = _activeSessionId;
            _activeSessionId = null;
            _continueSession = false;
            await FinalizeSessionQuietlyAsync(sessionId);
            DeleteDefaultAiSessionIfAny(sessionId);
            if (generation != _sessionFinalizeGeneration)
                return;
            Messages.Clear();
            RefreshWelcomeMessage();
            ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _sessionFinalizeGate.Release();
        }
    }

    private void DeleteDefaultAiSessionIfAny(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var session = _chat.GetSession(sessionId);
        if (session is null || !CharacterPresetService.IsDefaultAiSession(session.PresetKey))
            return;

        _chat.DeleteSession(sessionId);
    }

    /// <summary>長期記憶抽出＋タイトル確定。失敗しても新規会話は続行する。</summary>
    private async Task FinalizeSessionQuietlyAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;
        if (!Messages.Any(m => !m.IsWelcomePlaceholder))
            return;

        var savedMemories = 0;
        try
        {
            var session = _chat.GetSession(sessionId);
            if (session is not null && CharacterPresetService.IsDefaultAiSession(session.PresetKey))
            {
                // プレーンAIは長期記憶を持たない（会話セッション内の履歴のみ）
                _chat.DeleteSession(sessionId);
            }
            else
            {
                savedMemories = await _chat.FinalizeSessionWithMemoryCountAsync(sessionId, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Session finalize failed");
        }

        if (savedMemories > 0)
            SetStatusByKey("Chat.Status.MemorySaved", savedMemories);
    }

    public async Task FinalizeActiveSessionOnCloseAsync()
    {
        await _sessionFinalizeGate.WaitAsync();
        try
        {
            var sessionId = _activeSessionId;
            _activeSessionId = null;
            _continueSession = false;
            await FinalizeSessionQuietlyAsync(sessionId);
        }
        finally
        {
            _sessionFinalizeGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutateConversation))]
    private async Task ClearHistoryAsync()
    {
        await _sessionFinalizeGate.WaitAsync();
        try
        {
            // 消去前に事実を拾える場合は拾う
            var sessionId = _activeSessionId;
            _activeSessionId = null;
            _continueSession = false;
            await FinalizeSessionQuietlyAsync(sessionId);

            var historyDeleted = false;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                // Finalize で既に削除済みの場合もある
                if (_chat.GetSession(sessionId) is not null)
                {
                    _chat.DeleteSession(sessionId);
                    historyDeleted = true;
                }
                else
                {
                    historyDeleted = true;
                }
            }

            Messages.Clear();
            RefreshWelcomeMessage();
            if (historyDeleted)
                SetStatusByKey("Chat.Status.HistoryCleared", 1);
            else
                SetStatusByKey("Chat.Status.NewConversation");
            HasError = false;
            ErrorText = string.Empty;
            ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _sessionFinalizeGate.Release();
        }
    }

    /// <summary>左ペインの会話履歴から指定セッションを削除する。表示中なら画面もクリアする。</summary>
    public async Task DeleteConversationSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        if (string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
        {
            if (!CanMutateConversation)
            {
                NotifyBusyMutationBlocked();
                return;
            }

            await ClearHistoryAsync();
            return;
        }

        await _sessionFinalizeGate.WaitAsync();
        try
        {
            if (_chat.GetSession(sessionId) is not null)
                _chat.DeleteSession(sessionId);
            SetStatusByKey("Chat.Status.HistoryCleared", 1);
            ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _sessionFinalizeGate.Release();
        }
    }

    public event EventHandler? ConversationThreadsChanged;

    public void LoadConversationSession(string sessionId) =>
        _ = SwitchToConversationSessionAsync(sessionId);

    /// <summary>別スレッドへ移る前に、今の会話から長期記憶を抽出する。</summary>
    public async Task SwitchToConversationSessionAsync(string sessionId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(sessionId))
        {
            if (IsBusy)
                NotifyBusyMutationBlocked();
            return;
        }

        await _sessionFinalizeGate.WaitAsync();
        var generation = Interlocked.Increment(ref _sessionFinalizeGeneration);
        try
        {
            if (!string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
            {
                var previous = _activeSessionId;
                _activeSessionId = null;
                await FinalizeSessionQuietlyAsync(previous);
            }

            if (generation != _sessionFinalizeGeneration)
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
                _syncedCharacterFileName = CharacterPresetService.IsDefaultAiSession(session.PresetKey)
                    ? CharacterPresetService.NoneSelection
                    : session.PresetKey;
            }
            finally
            {
                _suppressCharacterChangeReset = false;
            }

            SetStatusByKey(Messages.Count > 0 ? "Chat.Status.ThreadLoaded" : "Chat.Status.ThreadEmpty");
            HasError = false;
            ErrorText = string.Empty;
        }
        finally
        {
            _sessionFinalizeGate.Release();
        }
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
        await AwaitUiFrameAsync();

        RunOnUi(() =>
        {
            IsBusy = true;
            NotifySendStopButtonLabelChanged();
            var mayFetchWeb = ChatMessageUrlExtractor.Extract(message, 1).Count > 0
                || ChatAgentResearchEnricher.LooksLikeResearchIntent(message);
            SetStatusByKey(mayFetchWeb ? "Chat.Status.FetchingWeb" : "Chat.Status.Generating");
            SendCommand.NotifyCanExecuteChanged();
            StopGenerationCommand.NotifyCanExecuteChanged();
        });

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
                await RunOnUiAsync(() =>
                {
                    switch (chunk.Type)
                    {
                        case "content":
                        {
                            EnsureAssistantLine();
                            replyAcc.Append(chunk.Text);
                            break;
                        }
                        case "reasoning" when UseReasoning:
                        {
                            EnsureAssistantLine();
                            reasoningAcc.Append(chunk.Text);
                            // 本文到着後も推論チャンクを捨てない（サーバが交互に流す場合がある）
                            SetStatusByKey("Chat.Status.Reasoning");
                            break;
                        }
                        case "done":
                            if (!string.IsNullOrWhiteSpace(chunk.Text))
                            {
                                EnsureAssistantLine();
                                replyAcc.Clear();
                                replyAcc.Append(chunk.Text);
                            }

                            // 完了時にまとめて渡される推論を優先（ストリーム中に消していた不具合の本命）
                            if (UseReasoning && !string.IsNullOrWhiteSpace(chunk.ReasoningText))
                            {
                                reasoningAcc.Clear();
                                reasoningAcc.Append(chunk.ReasoningText.Trim());
                            }

                            break;
                    }

                    if (assistantLine is null)
                        return;

                    if (UseReasoning && reasoningAcc.Length > 0)
                        assistantLine.SetReasoning(reasoningAcc.ToString());
                    else
                        assistantLine.ClearReasoning();

                    if (replyAcc.Length > 0 || !string.IsNullOrWhiteSpace(assistantLine.ReasoningText))
                        assistantLine.SetText(replyAcc.ToString());
                });
            }

            if (replyAcc.Length == 0)
                throw new InvalidOperationException(LocalizationService.Instance.Get("Chat.Status.EmptyReply"));

            replyText = replyAcc.ToString();
            var isDefaultAi = CharacterPresetService.IsNoneSelection(_characters.GetActivePresetFileName());
            await RunOnUiAsync(() =>
            {
                SetStatusByKey(!string.IsNullOrWhiteSpace(sessionId)
                    ? isDefaultAi
                        ? "Chat.Status.SessionContinued"
                        : "Chat.Status.SessionSaved"
                    : "Chat.Status.Done");

                if (!string.IsNullOrWhiteSpace(sessionId))
                    _continueSession = true;

                ConversationThreadsChanged?.Invoke(this, EventArgs.Empty);
                if (!isDefaultAi)
                    MainWindow.Instance?.EnsureConversationHistoryVisible();
            });

            if (!string.IsNullOrWhiteSpace(replyText))
                _ = _voicevoxSpeech.MaybeSpeakAssistantAsync(replyText);

            _ = RefreshHealthAsync();

            var activePreset = _characters.GetActivePresetFileName();
            var wantsPersonaUpdate = CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(message);
            if (wantsPersonaUpdate && CharacterPresetService.IsNoneSelection(activePreset))
            {
                await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.NeedNamedCharacter"));
            }
            else if (wantsPersonaUpdate && !_appearance.Current.CharacterSelfImproveEnabled)
            {
                await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.NeedEnabled"));
            }
            else if (_appearance.Current.CharacterSelfImproveEnabled
                && !CharacterPresetService.IsNoneSelection(activePreset)
                && !string.IsNullOrWhiteSpace(replyText))
            {
                _ = MaybeProposeSelfImproveAsync(activePreset, message, replyText, wantsPersonaUpdate);
            }
        }
        catch (OperationCanceledException) when (sendCt.IsCancellationRequested)
        {
            var loc = LocalizationService.Instance;
            var presetKey = CharacterPresetService.ResolveSessionPresetKey(_characters.GetActivePresetFileName());
            if (UseHistory && !string.IsNullOrWhiteSpace(sessionId))
                _chat.PersistCancelledUserMessage(sessionId, presetKey, req);
            else if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _chat.DeleteSessionIfNoMessages(sessionId);
                sessionId = null;
            }

            await RunOnUiAsync(() =>
            {
                if (sessionId is null)
                {
                    _activeSessionId = null;
                    _continueSession = false;
                }

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
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                SetError(ex);
                SetStatusByKey("Chat.Status.Error");
                if (assistantLine is null)
                    Messages.Add(new ChatLineViewModel("system", ErrorText));
                else if (string.IsNullOrEmpty(assistantLine.Text))
                    assistantLine.SetText(ErrorText);
                else
                    Messages.Add(new ChatLineViewModel("system", ErrorText));
            });
        }
        finally
        {
            await RunOnUiAsync(() =>
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
            });
        }
    }

    private async Task MaybeProposeSelfImproveAsync(
        string? presetFileName,
        string userMessage,
        string assistantReply,
        bool explicitRequest)
    {
        _selfImproveCts?.Cancel();
        _selfImproveCts?.Dispose();
        _selfImproveCts = new CancellationTokenSource();
        var ct = _selfImproveCts.Token;
        var holdBusyForDialog = false;

        await RunOnUiAsync(SetSelfImproveBusy);

        try
        {
            // 提案中は入力がロックされるので、明示依頼以外でも進捗を出す
            await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.Preparing"));

            var recentTurns = CollectRecentTurnsForSelfImprove();
            var proposal = await _selfImprove.TryProposeAfterReplyAsync(
                presetFileName,
                userMessage,
                assistantReply,
                ct,
                recentTurns);
            if (ct.IsCancellationRequested)
                return;

            if (proposal is null)
            {
                // Preparing のあとに黙って「完了」に戻すと、失敗に見えない
                await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.NoProposal"));
                return;
            }

            holdBusyForDialog = true;
            await RunOnUiAsync(() =>
            {
                SelfImproveProposed?.Invoke(this, proposal);
                // ハンドラがダイアログを取れなかった／未購読なら busy を戻す
                if (Volatile.Read(ref _selfImproveDialogGate) == 0)
                    ClearSelfImproveBusy();
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            /* 終了・次提案でキャンセル */
        }
        catch (TimeoutException)
        {
            if (!ct.IsCancellationRequested)
                await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.TimedOut"));
        }
        catch (Exception)
        {
            if (explicitRequest && !ct.IsCancellationRequested)
                await RunOnUiAsync(() => SetStatusByKey("Character.SelfImprove.Status.NoProposal"));
        }
        finally
        {
            if (!holdBusyForDialog)
                await RunOnUiAsync(ClearSelfImproveBusy);
        }
    }

    /// <summary>自己改善提案用に、直近の user/assistant 本文だけ集める（歓迎文・system は除外）。</summary>
    private IReadOnlyList<CharacterSelfImproveTranscript.Turn> CollectRecentTurnsForSelfImprove()
    {
        const int maxTurns = 8;
        var acc = new List<CharacterSelfImproveTranscript.Turn>(maxTurns);
        foreach (var line in Messages)
        {
            if (line.IsWelcomePlaceholder)
                continue;
            if (line.Role is not ("user" or "assistant"))
                continue;
            var text = (line.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;
            acc.Add(new CharacterSelfImproveTranscript.Turn(line.Role, text));
        }

        if (acc.Count <= maxTurns)
            return acc;
        return acc.Skip(acc.Count - maxTurns).ToList();
    }

    /// <summary>ウィンドウクローズ時に送信・提案を止める（llama Kill 前）。</summary>
    public void CancelBackgroundWorkOnClose()
    {
        try { _sendCts?.Cancel(); } catch { /* ignore */ }
        try { _selfImproveCts?.Cancel(); } catch { /* ignore */ }
        try { _voicevoxSpeech.Cancel(); } catch { /* ignore */ }
        ClearSelfImproveBusy();
    }

    public bool IsSelfImproveBusy => Volatile.Read(ref _selfImproveBusy) != 0;

    private void SetSelfImproveBusy()
    {
        Interlocked.Exchange(ref _selfImproveBusy, 1);
        NotifySelfImproveBusyChanged();
    }

    private void ClearSelfImproveBusy()
    {
        Interlocked.Exchange(ref _selfImproveBusy, 0);
        NotifySelfImproveBusyChanged();
    }

    private void NotifySelfImproveBusyChanged()
    {
        OnPropertyChanged(nameof(IsSelfImproveBusy));
        OnPropertyChanged(nameof(CanMutateConversation));
        OnPropertyChanged(nameof(IsInputEnabled));
        ClearHistoryCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>同意ダイアログが同時に複数出ないようにする。</summary>
    public bool TryEnterSelfImproveDialog() =>
        Interlocked.CompareExchange(ref _selfImproveDialogGate, 1, 0) == 0;

    public void ExitSelfImproveDialog()
    {
        Interlocked.Exchange(ref _selfImproveDialogGate, 0);
        ClearSelfImproveBusy();
    }

    public bool TryApplySelfImproveProposal(CharacterSelfImproveProposal proposal)
    {
        if (!_selfImprove.TryApplyApprovedProposal(proposal))
            return false;

        // 設定画面を開いている場合に備えて、次の表示で最新が載るようイベントは Settings 側の Reload に任せる
        return true;
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

    public bool IsInputEnabled => !IsBusy && !IsSelfImproveBusy;

    public bool CanMutateConversation => !IsBusy && !IsSelfImproveBusy;

    public void NotifyBusyMutationBlocked() => SetStatusByKey("Chat.Status.BusyCannotSwitch");

    private void NotifySendStopButtonLabelChanged() =>
        OnPropertyChanged(nameof(SendStopButtonLabel));

    partial void OnIsBusyChanged(bool value)
    {
        NotifySendStopButtonLabelChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanMutateConversation));
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private bool CanSend() =>
        !IsBusy && !IsSelfImproveBusy && (!string.IsNullOrWhiteSpace(InputText) || PendingAttachments.Count > 0);

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
        // InfoBar が既に開いていると同一失敗が目立たないため、いったん閉じて再開する
        if (HasError)
            HasError = false;
        HasError = true;
        ErrorText = UserFacingErrorLocalizer.Localize(ex);
    }

    public void ReportError(Exception ex) => SetError(ex);

    /// <summary>認識中の再クリックでキャンセルできるよう並行実行を許可する。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SpeechInputAsync()
    {
        if (!_speechInput.IsEnabled || IsBusy)
            return;

        if (_speechInput.IsListening)
        {
            _speechInput.Cancel();
            RefreshSpeechListeningUi();
            return;
        }

        try
        {
            var (text, languageTag) = await _speechInput.RecognizeOnceDetailedAsync();
            if (string.IsNullOrWhiteSpace(text))
                return;

            InputText = string.IsNullOrWhiteSpace(InputText)
                ? text
                : InputText.TrimEnd() + " " + text;

            // 英語 UI なのに日本語音声パックへ落ちたときだけ、一度案内する
            if (_loc.Current != AppLanguage.Japanese
                && languageTag is not null
                && languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                SetStatusByKey("Chat.SpeechInput.FallbackJapanese");
            }
        }
        catch (LocalizedServiceException ex)
        {
            SetError(ex);
        }
        catch (Exception)
        {
            SetError(new LocalizedServiceException("Chat.SpeechInput.Failed"));
        }
        finally
        {
            RefreshSpeechListeningUi();
        }
    }

    private void RefreshSpeechListeningUi()
    {
        OnPropertyChanged(nameof(IsSpeechListening));
        OnPropertyChanged(nameof(SpeechInputGlyph));
        UiSpeechInputTooltip = IsSpeechListening
            ? _loc.Get("Chat.SpeechInput.Tooltip.Listening")
            : _loc.Get("Chat.SpeechInput.Tooltip");
    }
}

public sealed record CharacterChoiceViewModel(string FileName, string DisplayName);
