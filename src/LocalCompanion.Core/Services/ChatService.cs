using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text;

namespace LocalCompanion.Services;

public sealed record ChatRequestDto(
    string Message,
    string[]? ImagesBase64 = null,
    string? AttachedText = null,
    string? AttachedFileName = null,
    bool UseRag = true,
    bool UseReasoning = false,
    bool UseHistory = true,
    string? SessionId = null,
    bool ContinueSession = false,
    IReadOnlyList<string>? WebSourceUrls = null,
    bool UsedAppearanceLocal = false,
    bool UsedAppearanceWeb = false);

public sealed record ClearHistoryRequest(string? PresetKey = null);

public sealed record ChatResponseDto(
    string Reply,
    bool UsedRag,
    string[]? RagSources,
    string? CharacterName = null,
    bool UsedReasoning = false,
    bool UsedHistory = false,
    bool UsedAttachment = false,
    string? ActiveModelFileName = null,
    string? SelectedModelFileName = null,
    string? LoadedModelFileName = null,
    bool ModelMismatch = false,
    string? ModelStatusMessage = null);

public sealed record ChatStreamChunkDto(
    string Type,
    string Text = "",
    string? Meta = null,
    string? CharacterName = null,
    bool Done = false,
    string? ReasoningText = null);

public sealed class ChatService
{
    private readonly LlamaServerClient _llama;
    private readonly RagService _rag;
    private readonly RagDatabase _db;
    private readonly CharacterRepository _character;
    private readonly CharacterPresetService _presets;
    private readonly ModelCatalogService _models;
    private readonly AppSettingsStore _appSettings;
    private readonly MemoryService _memory;
    private readonly ChatSearchService _chatSearch;
    private readonly LlamaOptions _opt;
    private readonly ChatExportPendingStore _exportPending = new();
    private readonly ChatHistoryDigestStore _digestStore;
    private readonly ChatHistoryDigestWriter _digestWriter;

    public ChatService(
        LlamaServerClient llama,
        RagService rag,
        RagDatabase db,
        CharacterRepository character,
        CharacterPresetService presets,
        ModelCatalogService models,
        AppSettingsStore appSettings,
        MemoryService memory,
        ChatSearchService chatSearch,
        IOptions<LlamaOptions> opt)
    {
        _llama = llama;
        _rag = rag;
        _db = db;
        _character = character;
        _presets = presets;
        _models = models;
        _appSettings = appSettings;
        _memory = memory;
        _chatSearch = chatSearch;
        _opt = opt.Value;
        _digestStore = new ChatHistoryDigestStore(db);
        _digestWriter = new ChatHistoryDigestWriter(_digestStore, new LlamaChatHistoryDigestLlm(llama));
    }

    /// <summary>保存済み会話セッション一覧（左ペイン用）。</summary>
    public IReadOnlyList<ConversationThreadPreview> ListThreadPreviews(int limit = 24)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.preset_key, s.title, s.updated_at,
                   (SELECT content FROM chat_messages m
                    WHERE m.session_id = s.id
                    ORDER BY m.id DESC LIMIT 1) AS last_content
            FROM conversation_sessions s
            WHERE s.preset_key <> $defaultAi
              AND EXISTS (
                SELECT 1 FROM chat_messages m2 WHERE m2.session_id = s.id
            )
            ORDER BY s.updated_at DESC
            LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$n", limit);
        cmd.Parameters.AddWithValue("$defaultAi", CharacterPresetService.DefaultAiPresetKey);

        var list = new List<ConversationThreadPreview>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var sessionId = r.GetString(0);
            var presetKey = r.GetString(1);
            var title = r.IsDBNull(2) ? "" : r.GetString(2);
            var lastAt = r.GetString(3);
            var snippet = r.IsDBNull(4) ? "" : r.GetString(4);
            list.Add(new ConversationThreadPreview(
                sessionId,
                presetKey,
                FormatSessionTitle(presetKey, title, snippet),
                TruncateContent(snippet, 60),
                lastAt));
        }

        return list;
    }

    public Task<IReadOnlyList<ChatSearchHit>> SearchMessagesAsync(
        string query,
        int topK = 12,
        CancellationToken ct = default) =>
        _chatSearch.SearchAsync(query, topK, ct);

    public ConversationSessionRecord? GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, preset_key, title, summary, created_at, updated_at, closed_at
            FROM conversation_sessions WHERE id = $id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        return new ConversationSessionRecord(
            r.GetString(0),
            r.GetString(1),
            r.IsDBNull(2) ? "" : r.GetString(2),
            r.IsDBNull(3) ? "" : r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6));
    }

    public string CreateSession(string presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
            throw new LocalizedServiceException("Chat.Error.NoCharacterSelected");

        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("O");
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_sessions (id, preset_key, title, summary, created_at, updated_at)
            VALUES ($id, $k, '', '', $t, $t)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$k", presetKey);
        cmd.Parameters.AddWithValue("$t", now);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>DB からセッションの会話を UI 表示用に読み込む。</summary>
    public IReadOnlyList<(string Role, string Content)> LoadSessionMessages(string sessionId, int maxMessages = 200)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<(string, string)>();

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT role, content FROM chat_messages
            WHERE session_id = $s
            ORDER BY id ASC
            LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$n", maxMessages);

        var rows = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1)));
        return rows;
    }

    /// <summary>セッションと紐づくメッセージを削除します。戻り値は削除したメッセージ行数です。</summary>
    public int DeleteSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return 0;

        using var conn = _db.Open();
        conn.Open();
        using var tx = conn.BeginTransaction();

        var idCmd = conn.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT id FROM chat_messages WHERE session_id = $s";
        idCmd.Parameters.AddWithValue("$s", sessionId);
        var messageIds = new List<long>();
        using (var reader = idCmd.ExecuteReader())
        {
            while (reader.Read())
                messageIds.Add(reader.GetInt64(0));
        }

        _chatSearch.DeleteIndexedMessages(conn, messageIds, tx);

        var delMessages = conn.CreateCommand();
        delMessages.Transaction = tx;
        delMessages.CommandText = "DELETE FROM chat_messages WHERE session_id = $s";
        delMessages.Parameters.AddWithValue("$s", sessionId);
        var n = delMessages.ExecuteNonQuery();

        var delSession = conn.CreateCommand();
        delSession.Transaction = tx;
        delSession.CommandText = "DELETE FROM conversation_sessions WHERE id = $s";
        delSession.Parameters.AddWithValue("$s", sessionId);
        delSession.ExecuteNonQuery();

        tx.Commit();
        return n;
    }

    public async Task FinalizeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var messages = LoadSessionMessages(sessionId, 40);
        if (messages.Count == 0)
        {
            DeleteSession(sessionId);
            return;
        }

        var title = await GenerateSessionTitleAsync(messages, ct);
        if (!string.IsNullOrWhiteSpace(title))
            UpdateSessionTitle(sessionId, title, title);

        await _memory.ExtractFromSessionAsync(sessionId, messages, ct);
        MarkSessionClosed(sessionId);
    }

    /// <summary>長期記憶抽出＋タイトル確定。新規に保存できた記憶件数を返す。</summary>
    public async Task<int> FinalizeSessionWithMemoryCountAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return 0;

        var messages = LoadSessionMessages(sessionId, 40);
        if (messages.Count == 0)
        {
            DeleteSession(sessionId);
            return 0;
        }

        var title = await GenerateSessionTitleAsync(messages, ct);
        if (!string.IsNullOrWhiteSpace(title))
            UpdateSessionTitle(sessionId, title, title);

        var saved = await _memory.ExtractFromSessionAsync(sessionId, messages, ct);
        MarkSessionClosed(sessionId);
        return saved;
    }

    /// <summary>セッションを閉じずに長期記憶だけ抽出する（デフォルトAI削除前など）。保存件数を返す。</summary>
    public Task<int> ExtractMemoriesFromSessionAsync(
        string sessionId,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct = default) =>
        _memory.ExtractFromSessionAsync(sessionId, messages, ct);

    private string FormatSessionTitle(string presetKey, string title, string lastSnippet)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (!string.IsNullOrWhiteSpace(lastSnippet))
            return TruncateContent(lastSnippet.Trim(), 32);

        return FormatCharacterName(presetKey);
    }

    private string FormatCharacterName(string presetKey)
    {
        if (CharacterPresetService.IsDefaultAiSession(presetKey))
            return LocalizationService.Instance.Get("Character.Default");

        try
        {
            var list = _presets.List();
            var hit = list.Presets.FirstOrDefault(p =>
                string.Equals(p.FileName, presetKey, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit.Name;
        }
        catch
        {
            /* ignore */
        }

        return Path.GetFileNameWithoutExtension(presetKey);
    }

    private void UpdateSessionTitle(string sessionId, string title, string summary)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE conversation_sessions
            SET title = $t, summary = $s, updated_at = $u
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$s", summary);
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    private void MarkSessionClosed(string sessionId)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE conversation_sessions
            SET closed_at = $c, updated_at = $c
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$c", now);
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    private void TouchSession(string sessionId, string? provisionalTitle = null)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(provisionalTitle))
        {
            cmd.CommandText = """
                UPDATE conversation_sessions
                SET updated_at = $u,
                    closed_at = NULL,
                    title = CASE WHEN title = '' THEN $t ELSE title END
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$t", TruncateContent(provisionalTitle.Trim(), 32));
        }
        else
        {
            cmd.CommandText = """
                UPDATE conversation_sessions
                SET updated_at = $u, closed_at = NULL
                WHERE id = $id
                """;
        }

        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    private async Task<string?> GenerateSessionTitleAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct)
    {
        if (!await _llama.PingAsync(ct))
            return FallbackSessionTitle(messages);

        var transcript = BuildSessionTranscript(messages, 2400);
        if (transcript.Length == 0)
            return null;

        var promptMessages = new List<ChatTurn>
        {
            new(
                "system",
                """
                You create short conversation titles for a chat history list.
                Output one concise title only (max 24 characters).
                Use the same language as the conversation.
                No quotes, no punctuation at the end, no explanation.
                """.Trim()),
            new("user", transcript),
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var title = await _llama.ChatAsync(
                promptMessages,
                temperature: 0.2,
                topP: 0.9,
                maxTokens: 48,
                useReasoning: false,
                ct: timeout.Token);
            return string.IsNullOrWhiteSpace(title) ? FallbackSessionTitle(messages) : title.Trim();
        }
        catch
        {
            return FallbackSessionTitle(messages);
        }
    }

    private static string? FallbackSessionTitle(IReadOnlyList<(string Role, string Content)> messages)
    {
        var firstUser = messages.FirstOrDefault(m => m.Role == "user").Content;
        return string.IsNullOrWhiteSpace(firstUser) ? null : TruncateContent(firstUser.Trim(), 32);
    }

    private static string BuildSessionTranscript(IReadOnlyList<(string Role, string Content)> messages, int maxChars)
    {
        var sb = new StringBuilder();
        foreach (var (role, content) in messages)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;
            var line = $"{role}: {content.Trim()}";
            if (sb.Length + line.Length + 1 > maxChars)
                break;
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(line);
        }

        return sb.ToString();
    }

    internal readonly record struct HistoryMode(bool Load, bool Save, string? PresetKey, string? SessionId);

    private HistoryMode ResolveHistory(ChatRequestDto req)
    {
        var sessionKey = CharacterPresetService.ResolveSessionPresetKey(_presets.GetActivePresetFileName());
        var session = string.IsNullOrWhiteSpace(req.SessionId) ? null : GetSession(req.SessionId);
        return ResolveHistoryCore(sessionKey, req.SessionId, req.UseHistory, session?.PresetKey);
    }

    /// <summary>キャラ切替後の履歴混在防止ロジック（単体テスト用）。</summary>
    internal static HistoryMode ResolveHistoryCore(
        string activeSessionKey,
        string? requestSessionId,
        bool useHistory,
        string? sessionPresetKey)
    {
        if (string.IsNullOrWhiteSpace(requestSessionId))
            return new(false, false, activeSessionKey, null);
        if (!useHistory)
            return new(false, false, activeSessionKey, requestSessionId);
        if (sessionPresetKey is null)
            return new(false, false, activeSessionKey, requestSessionId);
        // キャラ切替後に旧セッション ID が残っていても、別 preset へ混在保存しない
        if (!string.Equals(sessionPresetKey, activeSessionKey, StringComparison.OrdinalIgnoreCase))
            return new(false, false, activeSessionKey, requestSessionId);

        return new(true, true, activeSessionKey, requestSessionId);
    }

    public async Task<ChatResponseDto> ChatAsync(ChatRequestDto req, CancellationToken ct)
    {
        if (!await _llama.PingAsync(ct))
            throw new InvalidOperationException(LlamaServerClient.ConnectionFailedMessage);

        var historyMode = ResolveHistory(req);
        req = ExpandChoiceReply(req, historyMode);
        if (TryBuildPendingExportConflictReply(req, historyMode, out var conflictReply, out var conflictRagSources))
        {
            var conflictProfile = _character.Get();
            var conflictRuntime = await _models.GetRuntimeStatusAsync(_llama, ct);
            return new ChatResponseDto(
                conflictReply,
                conflictRagSources is { Length: > 0 },
                conflictRagSources ?? Array.Empty<string>(),
                conflictProfile.Name,
                req.UseReasoning,
                historyMode.Load,
                !string.IsNullOrWhiteSpace(req.AttachedText),
                conflictRuntime.ActiveModelFileName,
                conflictRuntime.SelectedModelFileName,
                conflictRuntime.LoadedModelFileName,
                conflictRuntime.ModelMismatch,
                conflictRuntime.StatusMessage);
        }

        req = await EnrichRequestWithWebContextAsync(req, historyMode, ct);
        var (effectiveMessage, exportRequest) = ResolveExportRequest(req, historyMode);
        var profile = _character.Get();
        // 履歴・出力の予算は、実際に起動中の llama-server ctx を優先して計算する。
        var effectiveContext = ResolveEffectiveContext(profile.ContextLength);
        var hasImage = req.ImagesBase64 is { Length: > 0 };
        var hasFile = !string.IsNullOrWhiteSpace(req.AttachedText);
        var heavyRequest = IsHeavyRequest(hasImage, hasFile, req.AttachedText, _opt.RagLightAttachMaxChars);
        var allowRagWithAttach = ChatRagAttachPolicy.Allow(
            req.UseRag,
            effectiveMessage,
            req.AttachedText,
            _rag.GetChunkCount(),
            _opt.RagLightAttachMaxChars);
        var runtime = await _models.GetRuntimeStatusAsync(_llama, ct);
        var modelFileName = runtime.ActiveModelFileName;
        var japaneseReply = TextScriptHelper.LooksJapanese(effectiveMessage);
        var isCharacter = !CharacterPresetService.IsNoneSelection(_presets.GetActivePresetFileName());
        var (systemParts, promptContext) = await BuildSystemPartsAsync(
            profile, runtime, effectiveMessage, japaneseReply, hasFile, hasImage, ct);
        _ = TryAppendHistoryDigest(systemParts, historyMode, japaneseReply);
        if (ChatAppearanceLookup.LooksLikeAppearanceRequest(req.Message))
            systemParts.Add(ChatSystemPromptTexts.AppearanceReferenceInstruction(japaneseReply));
        if (exportRequest is not null)
            systemParts.Add(ChatSystemPromptTexts.ExportHandoffInstruction(japaneseReply));

        string[]? ragSources = null;
        if (allowRagWithAttach)
        {
            var previousUser = historyMode.Load ? GetPreviousUserMessage(historyMode.SessionId) : null;
            var (ragResult, ragMode) = await TrySearchRagForChatAsync(effectiveMessage, previousUser, ct);
            if (ragMode != RagConversationMode.Skip
                && TryResolveVerbatimReply(ragResult, isCharacter, effectiveMessage, japaneseReply, ragMode, out var verbatimReply, out ragSources))
            {
                var finalVerbatim = await FinalizeReplyWithExportAsync(
                    verbatimReply, exportRequest, ragSources, japaneseReply, historyMode.SessionId, ct);
                finalVerbatim = ChatWebSourceCitation.AppendIfMissing(finalVerbatim, req.WebSourceUrls, japaneseReply);
                if (historyMode.Save
                    && !string.IsNullOrWhiteSpace(historyMode.SessionId)
                    && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
                {
                    SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
                    SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(finalVerbatim, HistorySaveMaxChars));
                    TouchSession(historyMode.SessionId, req.Message);
                }

                return new ChatResponseDto(
                    finalVerbatim,
                    ragSources is { Length: > 0 },
                    ragSources ?? Array.Empty<string>(),
                    profile.Name,
                    req.UseReasoning,
                    historyMode.Load,
                    hasFile,
                    modelFileName,
                    runtime.SelectedModelFileName,
                    runtime.LoadedModelFileName,
                    runtime.ModelMismatch,
                    runtime.StatusMessage);
            }

            if (ragResult is not null && ragMode != RagConversationMode.Skip)
                ragSources = TryAppendRagHits(systemParts, ragResult, ragMode, japaneseReply);
        }
        else if (!req.UseRag)
        {
            systemParts.Add(ChatSystemPromptTexts.RagDisabledNote(japaneseReply));
        }

        var userContent = BuildUserContent(req, _opt.MaxAttachTextChars, effectiveMessage);
        var systemText = ChatPromptAssembler.Compose(systemParts, promptContext, ragSources is { Length: > 0 });

        var attempts = BuildAttempts(heavyRequest, effectiveContext);
        string? reply = null;
        Exception? lastError = null;
        var usedHistory = false;
        var lastPick = ChatHistoryBudgetPick.Empty;

        foreach (var attempt in attempts)
        {
            var pick = historyMode.Load
                ? LoadSessionHistoryWithinBudget(
                    historyMode.SessionId!,
                    effectiveContext,
                    systemText.Length,
                    userContent.Length,
                    profile.MaxOutputTokens,
                    hasImage,
                    attempt.HistoryBudgetFraction)
                : ChatHistoryBudgetPick.Empty;
            lastPick = pick;
            if (pick.Kept.Count > 0)
                usedHistory = true;
            var messages = new List<ChatTurn> { new("system", systemText) };
            messages.AddRange(pick.Kept);
            messages.Add(new ChatTurn("user", userContent, req.ImagesBase64));

            var useReasoning = req.UseReasoning;
            var maxOut = ComputeSafeMaxTokens(
                RequestedOutputTokens(useReasoning, profile.MaxOutputTokens),
                effectiveContext,
                messages,
                hasImage,
                RequestedOutputTokens(useReasoning, attempt.MaxOutputCap));

            try
            {
                reply = await _llama.ChatAsync(
                    messages,
                    temperature: profile.Temperature,
                    topP: profile.TopP,
                    topK: profile.TopK,
                    maxTokens: maxOut,
                    useReasoning: useReasoning,
                    ct: ct);
                break;
            }
            catch (InvalidOperationException ex) when (useReasoning && LlamaServerClient.IsUnparsedChatFormat(ex.Message))
            {
                StartupLog.Write("chat reasoning output was not readable, retrying without it");
                reply = await _llama.ChatAsync(
                    messages,
                    temperature: profile.Temperature,
                    topP: profile.TopP,
                    topK: profile.TopK,
                    maxTokens: ComputeSafeMaxTokens(
                        profile.MaxOutputTokens,
                        effectiveContext,
                        messages,
                        hasImage,
                        attempt.MaxOutputCap),
                    useReasoning: false,
                    ct: ct);
                break;
            }
            catch (InvalidOperationException ex) when (LlamaServerClient.IsContextOverflowException(ex))
            {
                lastError = ex;
            }
        }

        if (reply is null)
            throw lastError ?? new InvalidOperationException(LlamaServerClient.ContextOverflowMessage);

        reply = ChatReplyLimitHelper.FinishReply(reply, _opt.MaxReplyChars, hitStreamCap: false, japaneseReply);
        reply = await FinalizeReplyWithExportAsync(
            reply, exportRequest, ragSources, japaneseReply, historyMode.SessionId, ct);
        reply = ChatWebSourceCitation.AppendIfMissing(reply, req.WebSourceUrls, japaneseReply);

        if (historyMode.Save
            && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
        {
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(reply, HistorySaveMaxChars));
            TouchSession(historyMode.SessionId, req.Message);
        }

        ScheduleHistoryDigestUpdate(historyMode, lastPick);

        return new ChatResponseDto(
            reply,
            ragSources is { Length: > 0 },
            ragSources,
            profile.Name,
            req.UseReasoning,
            usedHistory,
            hasFile,
            modelFileName,
            runtime.SelectedModelFileName,
            runtime.LoadedModelFileName,
            runtime.ModelMismatch,
            runtime.StatusMessage);
    }

    public async IAsyncEnumerable<ChatStreamChunkDto> StreamChatAsync(
        ChatRequestDto req,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!await _llama.PingAsync(ct))
            throw new InvalidOperationException(LlamaServerClient.ConnectionFailedMessage);

        var historyMode = ResolveHistory(req);
        req = ExpandChoiceReply(req, historyMode);
        if (TryBuildPendingExportConflictReply(req, historyMode, out var conflictReply, out var conflictRagSources))
        {
            var conflictProfile = _character.Get();
            var conflictRuntime = await _models.GetRuntimeStatusAsync(_llama, ct);
            yield return new ChatStreamChunkDto("content", conflictReply);
            var conflictMeta = formatMetaForStream(
                conflictRagSources is { Length: > 0 },
                req.UseReasoning,
                historyMode.Load,
                usedHistoryDigest: false,
                usedAttachment: !string.IsNullOrWhiteSpace(req.AttachedText),
                conflictRuntime.ActiveModelFileName,
                conflictRuntime);
            yield return new ChatStreamChunkDto(
                "done",
                conflictReply,
                Meta: conflictMeta,
                CharacterName: conflictProfile.Name,
                Done: true);
            yield break;
        }

        req = await EnrichRequestWithWebContextAsync(req, historyMode, ct);
        var (effectiveMessage, exportRequest) = ResolveExportRequest(req, historyMode);
        var profile = _character.Get();
        // 履歴・出力の予算は、実際に起動中の llama-server ctx を優先して計算する。
        var effectiveContext = ResolveEffectiveContext(profile.ContextLength);
        var hasImage = req.ImagesBase64 is { Length: > 0 };
        var hasFile = !string.IsNullOrWhiteSpace(req.AttachedText);
        var heavyRequest = IsHeavyRequest(hasImage, hasFile, req.AttachedText, _opt.RagLightAttachMaxChars);
        var allowRagWithAttach = ChatRagAttachPolicy.Allow(
            req.UseRag,
            effectiveMessage,
            req.AttachedText,
            _rag.GetChunkCount(),
            _opt.RagLightAttachMaxChars);
        var runtime = await _models.GetRuntimeStatusAsync(_llama, ct);
        var modelFileName = runtime.ActiveModelFileName;
        var japaneseReply = TextScriptHelper.LooksJapanese(effectiveMessage);
        var isCharacter = !CharacterPresetService.IsNoneSelection(_presets.GetActivePresetFileName());
        var (systemParts, promptContext) = await BuildSystemPartsAsync(
            profile, runtime, effectiveMessage, japaneseReply, hasFile, hasImage, ct);
        var usedHistoryDigest = TryAppendHistoryDigest(systemParts, historyMode, japaneseReply);
        if (ChatAppearanceLookup.LooksLikeAppearanceRequest(req.Message))
            systemParts.Add(ChatSystemPromptTexts.AppearanceReferenceInstruction(japaneseReply));
        if (exportRequest is not null)
            systemParts.Add(ChatSystemPromptTexts.ExportHandoffInstruction(japaneseReply));

        string[]? ragSources = null;
        string? verbatimReply = null;
        if (allowRagWithAttach)
        {
            var previousUser = historyMode.Load ? GetPreviousUserMessage(historyMode.SessionId) : null;
            var (ragResult, ragMode) = await TrySearchRagForChatAsync(effectiveMessage, previousUser, ct);
            if (ragMode != RagConversationMode.Skip
                && TryResolveVerbatimReply(ragResult, isCharacter, effectiveMessage, japaneseReply, ragMode, out verbatimReply, out ragSources))
            {
                // handled below
            }
            else if (ragResult is not null && ragMode != RagConversationMode.Skip)
            {
                ragSources = TryAppendRagHits(systemParts, ragResult, ragMode, japaneseReply);
            }
        }
        else if (!req.UseRag)
        {
            systemParts.Add(ChatSystemPromptTexts.RagDisabledNote(japaneseReply));
        }

        if (!string.IsNullOrWhiteSpace(verbatimReply))
        {
            var finalVerbatim = await FinalizeReplyWithExportAsync(
                verbatimReply, exportRequest, ragSources, japaneseReply, historyMode.SessionId, ct);
            finalVerbatim = ChatWebSourceCitation.AppendIfMissing(finalVerbatim, req.WebSourceUrls, japaneseReply);
            if (historyMode.Save
                && !string.IsNullOrWhiteSpace(historyMode.SessionId)
                && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
            {
                SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
                SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(finalVerbatim, HistorySaveMaxChars));
                TouchSession(historyMode.SessionId, req.Message);
            }

            yield return new ChatStreamChunkDto("content", finalVerbatim);
            var verbatimMeta = formatMetaForStream(
                ragSources is { Length: > 0 },
                req.UseReasoning,
                historyMode.Load,
                usedHistoryDigest: false,
                usedAttachment: hasFile,
                modelFileName,
                runtime,
                req.UsedAppearanceLocal,
                req.UsedAppearanceWeb);
            yield return new ChatStreamChunkDto(
                "done",
                finalVerbatim,
                Meta: verbatimMeta,
                CharacterName: profile.Name,
                Done: true);
            yield break;
        }

        var userContent = BuildUserContent(req, _opt.MaxAttachTextChars, effectiveMessage);
        var systemText = ChatPromptAssembler.Compose(systemParts, promptContext, ragSources is { Length: > 0 });
        var attempts = BuildAttempts(heavyRequest, effectiveContext);

        var replyBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var maxReplyChars = _opt.MaxReplyChars;
        var streamTruncated = false;
        var usedHistory = false;
        Exception? lastStreamError = null;
        var streamed = false;
        var lastPick = ChatHistoryBudgetPick.Empty;
        var forcePlainReply = false;
        var attemptIndex = 0;

        while (attemptIndex < attempts.Count)
        {
            var attempt = attempts[attemptIndex];
            replyBuilder.Clear();
            reasoningBuilder.Clear();
            streamTruncated = false;

            var pick = historyMode.Load
                ? LoadSessionHistoryWithinBudget(
                    historyMode.SessionId!,
                    effectiveContext,
                    systemText.Length,
                    userContent.Length,
                    profile.MaxOutputTokens,
                    hasImage,
                    attempt.HistoryBudgetFraction)
                : ChatHistoryBudgetPick.Empty;
            lastPick = pick;
            usedHistory = pick.Kept.Count > 0;
            var messages = new List<ChatTurn> { new("system", systemText) };
            messages.AddRange(pick.Kept);
            messages.Add(new ChatTurn("user", userContent, req.ImagesBase64));
            var useReasoning = req.UseReasoning && !forcePlainReply;
            var outputCap = RequestedOutputTokens(useReasoning, attempt.MaxOutputCap);
            var maxOut = ComputeSafeMaxTokens(
                RequestedOutputTokens(useReasoning, profile.MaxOutputTokens),
                effectiveContext,
                messages,
                hasImage,
                outputCap);

            var stream = _llama.StreamChatAsync(
                messages,
                temperature: profile.Temperature,
                topP: profile.TopP,
                topK: profile.TopK,
                maxTokens: maxOut,
                useReasoning: useReasoning,
                ct: ct);
            await using var streamEnumerator = stream.GetAsyncEnumerator(ct);
            var attemptOverflow = false;

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await streamEnumerator.MoveNextAsync();
                }
                catch (InvalidOperationException ex) when (LlamaServerClient.IsContextOverflowException(ex))
                {
                    lastStreamError = ex;
                    attemptOverflow = true;
                    break;
                }
                catch (InvalidOperationException ex) when (useReasoning && LlamaServerClient.IsUnparsedChatFormat(ex.Message))
                {
                    StartupLog.Write("chat reasoning output was not readable, retrying without it");
                    forcePlainReply = true;
                    replyBuilder.Clear();
                    reasoningBuilder.Clear();
                    break;
                }

                if (!hasNext)
                {
                    streamed = true;
                    break;
                }

                var piece = streamEnumerator.Current;
                if (piece.Kind == "timings")
                {
                    yield return new ChatStreamChunkDto("timings", piece.Text);
                }
                else if (piece.Kind == "reasoning")
                {
                    reasoningBuilder.Append(piece.Text);
                    if (req.UseReasoning)
                        yield return new ChatStreamChunkDto("reasoning", piece.Text);
                }
                else if (piece.Kind == "content")
                {
                    if (replyBuilder.Length >= maxReplyChars)
                    {
                        streamTruncated = true;
                        streamed = true;
                        break;
                    }

                    var pieceText = piece.Text;
                    var remaining = maxReplyChars - replyBuilder.Length;
                    if (remaining <= 0)
                    {
                        streamTruncated = true;
                        streamed = true;
                        break;
                    }

                    if (pieceText.Length > remaining)
                    {
                        pieceText = pieceText[..remaining];
                        streamTruncated = true;
                    }

                    if (pieceText.Length > 0)
                    {
                        replyBuilder.Append(pieceText);
                        yield return new ChatStreamChunkDto("content", pieceText);
                    }

                    if (streamTruncated)
                    {
                        streamed = true;
                        break;
                    }
                }
            }

            if (streamed)
                break;
            if (!(forcePlainReply && useReasoning))
                attemptIndex++;
            if (attemptOverflow)
                continue;
        }

        if (!streamed)
            throw lastStreamError ?? new InvalidOperationException(LlamaServerClient.ContextOverflowMessage);

        var reply = ChatReplyLimitHelper.FinishReply(
            replyBuilder.ToString().Trim(),
            maxReplyChars,
            streamTruncated,
            japaneseReply);
        var reasoning = reasoningBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(reply) && string.IsNullOrWhiteSpace(reasoning))
            throw new LocalizedServiceException("Chat.Error.EmptyModelReply");

        reply = await FinalizeReplyWithExportAsync(
            reply, exportRequest, ragSources, japaneseReply, historyMode.SessionId, ct);
        reply = ChatWebSourceCitation.AppendIfMissing(reply, req.WebSourceUrls, japaneseReply);

        if (historyMode.Save
            && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
        {
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
            var historyReply = !string.IsNullOrWhiteSpace(reply) ? reply : reasoning;
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(historyReply, HistorySaveMaxChars));
            TouchSession(historyMode.SessionId, req.Message);
        }

        ScheduleHistoryDigestUpdate(historyMode, lastPick);

        var meta = formatMetaForStream(
            ragSources is { Length: > 0 },
            req.UseReasoning,
            usedHistory,
            usedHistoryDigest,
            hasFile,
            modelFileName,
            runtime,
            req.UsedAppearanceLocal,
            req.UsedAppearanceWeb);
        yield return new ChatStreamChunkDto(
            "done",
            reply,
            Meta: meta,
            CharacterName: profile.Name,
            Done: true,
            ReasoningText: string.IsNullOrWhiteSpace(reasoning) ? null : reasoning);
    }

    private static string formatMetaForStream(
        bool usedRag,
        bool usedReasoning,
        bool usedHistory,
        bool usedHistoryDigest,
        bool usedAttachment,
        string? modelFileName,
        ModelRuntimeStatus runtime,
        bool usedAppearanceLocal = false,
        bool usedAppearanceWeb = false)
    {
        var loc = LocalizationService.Instance;
        var parts = new List<string>();
        if (runtime.ModelMismatch) parts.Add(loc.Get("Chat.Meta.ModelMismatch"));
        if (usedRag) parts.Add(loc.Get("Chat.Meta.Rag"));
        if (usedReasoning) parts.Add(loc.Get("Chat.Meta.ReasoningOn"));
        if (usedAttachment) parts.Add(loc.Get("Chat.Meta.Attachment"));
        if (usedHistory) parts.Add(loc.Get("Chat.Meta.History"));
        if (usedHistoryDigest) parts.Add(loc.Get("Chat.Meta.HistoryDigest"));
        if (usedAppearanceLocal) parts.Add(loc.Get("Chat.Meta.AppearanceLocal"));
        if (usedAppearanceWeb) parts.Add(loc.Get("Chat.Meta.AppearanceWeb"));
        if (!string.IsNullOrWhiteSpace(modelFileName)) parts.Add(loc.Format("Chat.Meta.LoadingModel", modelFileName));
        if (runtime.ModelMismatch && !string.IsNullOrWhiteSpace(runtime.StatusMessage)) parts.Add(runtime.StatusMessage);
        return parts.Count > 0 ? string.Join(" · ", parts) : loc.Get("Chat.Meta.MessageOnly");
    }

    private const int MaxHistoryMessagesToScan = 500;

    private int HistorySaveMaxChars => Math.Clamp(_opt.MaxReplyChars, 1024, 20000);

    private IReadOnlyList<(double HistoryBudgetFraction, int MaxOutputCap)> BuildAttempts(
        bool heavyRequest,
        int contextLength)
    {
        var outputCap = Math.Clamp(_opt.MaxReplyOutputTokens, 512, 8192);
        if (contextLength <= 8192)
            outputCap = Math.Min(outputCap, 2048);

        if (heavyRequest)
        {
            return
            [
                (0.6, Math.Min(outputCap, 2048)),
                (0.35, 1536),
                (0.15, 1024),
                (0, 1024)
            ];
        }

        if (contextLength <= 8192)
        {
            return
            [
                (0.35, outputCap),
                (0.15, Math.Min(outputCap, 1536)),
                (0, 1024)
            ];
        }

        return
        [
            (1.0, outputCap),
            (0.65, Math.Min(outputCap, 4096)),
            (0.35, 2048),
            (0, 2048)
        ];
    }

    internal static int RequestedOutputTokens(bool useReasoning, int answerTokens)
    {
        if (!useReasoning || answerTokens <= 0)
            return answerTokens;
        return answerTokens + LlamaServerNativeHost.GemmaReasoningBudgetTokens;
    }

    private static int ComputeSafeMaxTokens(
        int requested,
        int contextLength,
        IReadOnlyList<ChatTurn> messages,
        bool hasImage,
        int hardCap)
    {
        var estChars = messages.Sum(m => m.Content.Length);
        if (hasImage || messages.Any(m => m.ImagesBase64 is { Length: > 0 }))
            estChars += 7500;

        var promptTokens = estChars / 2 + 384;
        var budget = contextLength - promptTokens - 128;
        var capped = Math.Min(requested, Math.Min(hardCap, budget));
        return Math.Clamp(capped, 256, hardCap);
    }

    private static int ResolveEffectiveContext(int requestedContext)
    {
        var capped = LlamaContextPolicy.CapForServer(requestedContext);
        var marker = Path.Combine(AppPaths.Current.ToolsDirectory, ".last-ctx");
        try
        {
            if (File.Exists(marker)
                && int.TryParse(File.ReadAllText(marker).Trim(), out var runningContext)
                && runningContext >= 2048)
            {
                return Math.Min(capped, runningContext);
            }
        }
        catch
        {
            // If the marker is unavailable, fall back to the configured policy.
        }

        return capped;
    }

    private static string SummarizeForHistory(ChatRequestDto req)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.AttachedFileName))
            parts.Add($"[📎 {req.AttachedFileName}]");
        if (req.ImagesBase64 is { Length: > 0 })
            parts.Add($"[🖼 画像×{req.ImagesBase64.Length}]");
        parts.Add(req.Message);
        return string.Join(" ", parts);
    }

    /// <summary>材料（設定・長期記憶）を集めて <see cref="ChatPromptAssembler"/> に渡す。組み立ての判断はアセンブラ側。</summary>
    private async Task<(List<string> Parts, ChatPromptContext Context)> BuildSystemPartsAsync(
        CharacterProfileDto profile,
        ModelRuntimeStatus runtime,
        string userMessage,
        bool japaneseReply,
        bool hasAttachment,
        bool hasImage,
        CancellationToken ct)
    {
        var isCharacter = !CharacterPresetService.IsNoneSelection(_presets.GetActivePresetFileName());
        var userDisplayName = _appSettings.Load().UserDisplayName?.Trim();

        string? memoryBlock = null;
        var sessionPresetKey = CharacterPresetService.ResolveSessionPresetKey(_presets.GetActivePresetFileName());
        try
        {
            var memories = await _memory.GetRelevantForPromptAsync(userMessage, sessionPresetKey, ct);
            memoryBlock = MemoryService.FormatForSystemPrompt(memories, japaneseReply);
        }
        catch
        {
            /* memory is optional */
        }

        var context = new ChatPromptContext(
            isCharacter,
            profile,
            userDisplayName,
            memoryBlock,
            runtime,
            hasAttachment,
            hasImage,
            japaneseReply,
            userMessage,
            _opt.MaxReplyChars);
        return (ChatPromptAssembler.BuildCoreParts(context), context);
    }

    private static string BuildUserContent(ChatRequestDto req, int maxAttachChars, string? messageOverride = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.AttachedText))
        {
            var name = req.AttachedFileName ?? "添付";
            var body = req.AttachedText.Trim();
            if (body.Length > maxAttachChars)
            {
                body = body[..maxAttachChars] +
                       $"\n\n…（{name} は長いため先頭 {maxAttachChars} 文字のみ表示しています。全文は設定の RAG タブから登録してください）";
            }
            parts.Add($"【添付: {name}】\n{body}");
        }
        if (req.ImagesBase64 is { Length: > 0 })
        {
            parts.Add("（画像が添付されています。描写と、写っている文字の読み取り（OCR）の両方を答えてください）");
            var embedded = EmbeddedGenerationPrompt.FormatForChat(req.ImagesBase64);
            if (embedded is not null)
                parts.Add(embedded);
        }
        parts.Add(messageOverride ?? req.Message);
        return string.Join("\n\n", parts);
    }

    private async Task<ChatRequestDto> EnrichRequestWithWebContextAsync(
        ChatRequestDto req,
        HistoryMode historyMode,
        CancellationToken ct)
    {
        // 1) メッセージ内 URL を自動取得
        req = await ChatInlineWebContextBuilder.EnrichAsync(
            req,
            Math.Max(1, _opt.InlineUrlMaxCount),
            Math.Max(1000, _opt.InlineUrlMaxChars),
            Math.Max(1000, _opt.MaxAttachTextChars),
            ct);

        req = await ChatAppearanceLookupEnricher.EnrichAsync(
            req,
            _presets.LoadAllNamedPresets(),
            _opt.WebSearchEnabled,
            _opt.WebSearchBaseUrl,
            Math.Clamp(_opt.WebSearchTopK, 1, 8),
            Math.Clamp(_opt.WebSearchFetchTopK, 0, 4),
            Math.Max(1000, _opt.InlineUrlMaxChars),
            Math.Max(1000, _opt.MaxAttachTextChars),
            ct);

        // 見た目参照のターンは、見つからなくても汎用 Web 調査に流さない。
        if (ChatAppearanceLookup.LooksLikeAppearanceRequest(req.Message))
            return req;

        var previousUser = historyMode.Load ? GetPreviousUserMessage(historyMode.SessionId) : null;
        req = await ChatAgentResearchEnricher.EnrichAsync(
            req,
            _opt.WebSearchEnabled,
            _opt.WebSearchBaseUrl,
            Math.Clamp(_opt.WebSearchTopK, 1, 8),
            Math.Clamp(_opt.WebSearchFetchTopK, 0, 4),
            Math.Max(1000, _opt.InlineUrlMaxChars),
            Math.Max(1000, _opt.MaxAttachTextChars),
            previousUser,
            ct);

        return req;
    }

    private (string EffectiveMessage, ChatExportRequest? Export) ResolveExportRequest(
        ChatRequestDto req,
        HistoryMode historyMode)
    {
        if (ChatExportRequestParser.TryParse(req.Message, out var export))
            return (export.Query, export);

        if (historyMode.Load && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && ChatExportRequestParser.TryInheritRepeatExport(
                req.Message,
                GetRecentUserMessageContents(historyMode.SessionId),
                out export))
        {
            return (export.Query, export);
        }

        return (req.Message, null);
    }

    private List<string> GetRecentUserMessageContents(string sessionId, int maxRows = 24)
    {
        var rows = LoadRecentSessionRows(sessionId, maxRows);
        var users = new List<string>();
        foreach (var row in rows)
        {
            if (string.Equals(row.Role, "user", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.Content))
            {
                users.Add(row.Content);
            }
        }

        return users;
    }

    private bool TryBuildPendingExportConflictReply(
        ChatRequestDto req,
        HistoryMode historyMode,
        out string reply,
        out string[]? ragSources)
    {
        reply = "";
        ragSources = null;
        if (!_exportPending.TryResolveConflictContinuation(
                historyMode.SessionId, req.Message, out var pending, out var policy))
        {
            return false;
        }

        var export = pending.Request with { ConflictPolicy = policy };
        var japanese = TextScriptHelper.LooksJapanese(pending.Request.Query);
        reply = ChatTextExporter.CompleteExportNotice(
            pending.CleanReply,
            export,
            pending.Document,
            pending.RagSources,
            japanese,
            _exportPending,
            historyMode.SessionId);
        ragSources = pending.RagSources;

        if (historyMode.Save
            && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
        {
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(reply, HistorySaveMaxChars));
            TouchSession(historyMode.SessionId, req.Message);
        }

        return true;
    }

    private async Task<string> FinalizeReplyWithExportAsync(
        string reply,
        ChatExportRequest? export,
        string[]? ragSources,
        bool japaneseReply,
        string? sessionId,
        CancellationToken ct)
    {
        if (export is null)
            return reply;

        var cleaned = ChatExportReplySanitizer.StripFakeSaveClaims(reply);
        return await ChatTextExporter.AppendExportNoticeAsync(
            _llama, cleaned, export, ragSources, japaneseReply, _exportPending, sessionId, ct);
    }

    /// <summary>キャンセル時にユーザーメッセージだけ履歴へ残す。</summary>
    public void PersistCancelledUserMessage(string sessionId, string presetKey, ChatRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(presetKey))
            return;

        SaveMessage(sessionId, presetKey, "user", SummarizeForHistory(req));
        TouchSession(sessionId, req.Message);
    }

    public void DeleteSessionIfNoMessages(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;
        if (LoadSessionMessages(sessionId, 1).Count == 0)
            DeleteSession(sessionId);
    }

    private void SaveMessage(string sessionId, string presetKey, string role, string content)
    {
        var messageId = InsertMessage(sessionId, presetKey, role, content);
        if (messageId > 0)
            _ = IndexMessageInBackground(messageId, content);
    }

    private long InsertMessage(string sessionId, string presetKey, string role, string content)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_messages (preset_key, session_id, role, content, created_at)
            VALUES ($k, $s, $r, $c, $t)
            """;
        cmd.Parameters.AddWithValue("$k", presetKey);
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$r", role);
        cmd.Parameters.AddWithValue("$c", content);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (long)(idCmd.ExecuteScalar() ?? 0L);
    }

    private async Task IndexMessageInBackground(long messageId, string content)
    {
        try
        {
            await _chatSearch.IndexMessageAsync(messageId, content).ConfigureAwait(false);
        }
        catch
        {
            /* indexing is best-effort */
        }
    }

    private List<ChatHistoryRow> LoadRecentSessionRows(string sessionId, int maxRows)
    {
        if (maxRows <= 0 || string.IsNullOrWhiteSpace(sessionId))
            return [];

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, role, content FROM chat_messages
            WHERE session_id = $s
            ORDER BY id DESC LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$n", maxRows);

        var rows = new List<ChatHistoryRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ChatHistoryRow(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return rows;
    }

    /// <summary>セッション内の直近メッセージを、コンテキスト余力の範囲で古い順に返します。</summary>
    private ChatHistoryBudgetPick LoadSessionHistoryWithinBudget(
        string sessionId,
        int contextLength,
        int systemCharCount,
        int currentUserCharCount,
        int reservedOutputTokens,
        bool currentMessageHasImage,
        double budgetFraction)
    {
        if (budgetFraction <= 0 || string.IsNullOrWhiteSpace(sessionId))
            return ChatHistoryBudgetPick.Empty;

        var imageOverhead = currentMessageHasImage ? 7500 : 0;
        var fixedChars = systemCharCount + currentUserCharCount + imageOverhead;
        var fixedTokens = fixedChars / 2 + 384;
        var outputReserve = Math.Clamp(reservedOutputTokens, 256, 8192);
        var totalBudget = contextLength - outputReserve - 128;
        var historyTokenBudget = (int)((totalBudget - fixedTokens) * budgetFraction);
        if (historyTokenBudget < 128)
            return ChatHistoryBudgetPick.Empty;

        var historyCharBudget = Math.Max(256, historyTokenBudget * 2);
        var rows = LoadRecentSessionRows(sessionId, MaxHistoryMessagesToScan);
        return ChatHistoryDigest.Pick(rows, historyCharBudget);
    }

    private bool TryAppendHistoryDigest(List<string> systemParts, HistoryMode historyMode, bool japanese)
    {
        if (!historyMode.Load
            || string.IsNullOrWhiteSpace(historyMode.SessionId)
            || !ChatHistoryDigest.IsAllowedPreset(historyMode.PresetKey)
            || !_appSettings.Load().HistoryDigestEnabled)
            return false;

        var (digest, _) = _digestStore.Read(historyMode.SessionId);
        return ChatHistoryDigest.TryAppendPrompt(systemParts, digest, japanese);
    }

    private void ScheduleHistoryDigestUpdate(HistoryMode historyMode, ChatHistoryBudgetPick pick)
    {
        if (!historyMode.Load
            || string.IsNullOrWhiteSpace(historyMode.SessionId)
            || !ChatHistoryDigest.IsAllowedPreset(historyMode.PresetKey)
            || !_appSettings.Load().HistoryDigestEnabled
            || pick.Dropped.Count == 0)
            return;

        var sessionId = historyMode.SessionId;
        var dropped = pick.Dropped;
        _ = Task.Run(async () =>
        {
            try
            {
                await _digestWriter.TryUpdateAsync(sessionId, dropped, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, "History digest update failed");
            }
        });
    }

    /// <summary>
    /// 参考資料を system に足す。ヒットがあれば共通指示 1 本＋回答ルール表（<see cref="RagAnswerPolicy"/>）の追加指示＋予算内の本文。
    /// ヒットが無いときは表に従って「資料なし」を言うか、何も言わない。戻り値は UI 用の出典ラベル（ヒット無しは null）。
    /// </summary>
    private string[]? TryAppendRagHits(
        List<string> systemParts,
        RagSearchResult ragResult,
        RagConversationMode conversationMode,
        bool japanese)
    {
        var hits = ragResult.Hits;
        var plan = ragResult.Plan;
        if (hits.Count == 0)
        {
            var empty = RagAnswerPolicy.ChooseEmptyHitInstruction(conversationMode, ragResult.SearchFailed, japanese);
            if (empty is not null)
                systemParts.Add(empty);
            return null;
        }

        var rules = RagAnswerPolicy.Decide(plan, conversationMode, hits);
        systemParts.Add(ChatSystemPromptTexts.RagGroundingInstruction(japanese));
        if (rules.Legal)
            systemParts.Add(ChatSystemPromptTexts.RagLegalAddendum(japanese, rules.QuotePenalty, rules.SingleArticle));
        if (rules.Risk)
            systemParts.Add(ChatSystemPromptTexts.RagRiskCautionInstruction(japanese));
        if (rules.Advisory)
            systemParts.Add(ChatSystemPromptTexts.RagAdvisoryInstruction(japanese));

        var block = RagPromptBudget.FormatHits(hits, plan, _opt.RagPromptHitMaxChars, _opt.RagPromptBlockMaxChars);
        systemParts.Add(ChatSystemPromptTexts.RagHitsHeader(japanese) + "\n" + block);
        return hits.Select((h, i) => h.FormatSourceLabel(i)).ToArray();
    }

    private static bool IsHeavyRequest(bool hasImage, bool hasFile, string? attachedText, int lightAttachMaxChars)
    {
        if (hasImage)
            return true;
        if (!hasFile)
            return false;
        return (attachedText?.Length ?? 0) > lightAttachMaxChars;
    }

    private string BuildRagSearchQuery(string currentMessage, HistoryMode historyMode)
    {
        var previousUser = historyMode.Load
            ? GetPreviousUserMessage(historyMode.SessionId)
            : null;
        return RagSearchQueryComposer.Compose(currentMessage, previousUser);
    }

    private string? GetPreviousUserMessage(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return RagHistoryTopicPicker.PickPreviousUserTopic(
            LoadRecentSessionRows(sessionId, 24).Select(row => (row.Role, row.Content)));
    }

    private ChatRequestDto ExpandChoiceReply(ChatRequestDto req, HistoryMode historyMode)
    {
        var lastAssistant = historyMode.Load ? GetLastAssistantMessage(historyMode.SessionId) : null;
        var expanded = ChatChoiceReplyExpander.Expand(req.Message, lastAssistant);
        return string.Equals(expanded, req.Message, StringComparison.Ordinal)
            ? req
            : req with { Message = expanded };
    }

    private string? GetLastAssistantMessage(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        foreach (var row in LoadRecentSessionRows(sessionId, 24))
        {
            if (row.Role == "assistant" && !string.IsNullOrWhiteSpace(row.Content))
                return row.Content;
        }

        return null;
    }

    private static bool TryResolveVerbatimReply(
        RagSearchResult? ragResult,
        bool isCharacter,
        string userMessage,
        bool japanese,
        RagConversationMode conversationMode,
        out string reply,
        out string[]? sources)
    {
        reply = "";
        sources = null;
        if (ragResult is null)
            return false;

        // 危険話題はキャラでも LLM 合成より先に機械引用（推論 OFF でも罰則を落とさない）
        if (RagRiskCautionResponder.TryFormat(
                conversationMode, ragResult.Hits, userMessage, japanese, out reply))
        {
            sources = ragResult.Hits.Select((h, i) => h.FormatSourceLabel(i)).ToArray();
            return true;
        }

        if (!RagArticleAnswerPolicy.AllowCharacterVerbatim(isCharacter, ragResult.Plan, userMessage))
            return false;

        if (RagArticleHitFilter.IsAmbiguousSources(ragResult.Plan, ragResult.Hits))
        {
            var names = RagArticleHitFilter.DistinctMatchingSources(ragResult.Plan, ragResult.Hits);
            var lang = LocalizationService.Instance?.Current
                ?? (japanese ? AppLanguage.Japanese : AppLanguage.English);
            reply = RagArticleAmbiguousFormatter.Format(ragResult.Plan, names, lang);
            sources = names.ToArray();
            return true;
        }

        if (ragResult.Plan.ResponseMode == RagResponseMode.Verbatim
            && RagVerbatimResponder.TryFormat(ragResult.Plan, ragResult.Hits, japanese, out reply))
        {
            sources = ragResult.Hits.Select((h, i) => h.FormatSourceLabel(i)).ToArray();
            return true;
        }

        if (RagArticleHitFilter.IsMiss(ragResult.Plan, ragResult.Hits)
            || RagVerbatimGuard.ShouldBlockLlm(ragResult.Plan))
        {
            reply = RagVerbatimGuard.BuildMissReply(ragResult.Plan, japanese);
            sources = Array.Empty<string>();
            return true;
        }

        return false;
    }

    private async Task<(RagSearchResult? Result, RagConversationMode Mode)> TrySearchRagForChatAsync(
        string currentMessage,
        string? previousUserMessage,
        CancellationToken ct)
    {
        var plan = RagQueryPlanner.Plan(currentMessage, previousUserMessage);
        var mode = RagConversationGate.Resolve(plan, currentMessage);
        if (mode == RagConversationMode.Skip)
            return (null, mode);
        if (mode == RagConversationMode.Probe)
            mode = await LayaSidecar.TryUpgradeProbeAsync(currentMessage, ct);

        var result = await TrySearchRagWithPlanAsync(currentMessage, previousUserMessage, plan, mode, ct);
        if (result is null)
        {
            // 呼び出し側キャンセルなど。条文は創作ブロック、一般も沈黙させず未完了扱い。
            return (new RagSearchResult(Array.Empty<RagSearchHit>(), plan, SearchFailed: true), mode);
        }

        return (RagConversationGate.ApplyHitPolicy(result, currentMessage, mode), mode);
    }

    /// <summary>俯瞰は LLM を予算内で何度か呼ぶので、その予算＋余裕まで待つ。</summary>
    internal TimeSpan ResolveRagSearchTimeout(RagQueryPlan plan, bool lexicalOnly)
    {
        if (lexicalOnly)
            return TimeSpan.FromSeconds(3);
        if (plan.Intent == RagQueryIntent.Overview)
            return TimeSpan.FromMilliseconds(Math.Max(12_000, _opt.RagGraphTimeBudgetMs + 6_000));
        return TimeSpan.FromSeconds(12);
    }

    private async Task<RagSearchResult?> TrySearchRagWithPlanAsync(
        string currentMessage,
        string? previousUserMessage,
        RagQueryPlan plan,
        RagConversationMode mode,
        CancellationToken ct)
    {
        try
        {
            // Probe は毎ターン走るので埋め込みを呼ばず語彙だけ・短い上限
            var lexicalOnly = mode == RagConversationMode.Probe;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ResolveRagSearchTimeout(plan, lexicalOnly));
            return await _rag.SearchWithPlanAsync(
                currentMessage, previousUserMessage, _opt.RagTopK, timeout.Token, lexicalOnly);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StartupLog.Write("RAG search timed out.");
            return new RagSearchResult(Array.Empty<RagSearchHit>(), plan, SearchFailed: true);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "RAG search failed");
            return new RagSearchResult(Array.Empty<RagSearchHit>(), plan, SearchFailed: true);
        }
    }

    private async Task<IReadOnlyList<RagSearchHit>> TrySearchRagAsync(string message, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            return await _rag.SearchAsync(message, _opt.RagTopK, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StartupLog.Write("RAG search timed out.");
            return Array.Empty<RagSearchHit>();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "RAG search failed");
            return Array.Empty<RagSearchHit>();
        }
    }

    private static string TruncateContent(string content, int maxChars)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxChars)
            return content;
        return content[..maxChars] + "\n…（履歴省略）";
    }

    /// <summary>画面表示用の返答を、VOICEVOX 読み上げ用の自然な日本語に翻訳します（履歴には保存しません）。</summary>
    public async Task<string?> TranslateForJapaneseSpeechAsync(string displayText, CancellationToken ct = default)
    {
        if (!await _llama.PingAsync(ct))
            return null;

        var trimmed = displayText.Trim();
        if (trimmed.Length == 0)
            return null;

        const int maxSourceChars = 2400;
        var source = trimmed.Length > maxSourceChars ? trimmed[..maxSourceChars] : trimmed;

        var messages = new List<ChatTurn>
        {
            new(
                "system",
                """
                あなたは翻訳者です。入力は AI アシスタントの返答文（画面表示用）です。
                意味を保った自然な日本語の本文だけを出力してください。
                前置き・説明・英語・記号装飾・引用符は付けないでください。翻訳結果のみを1つ出力してください。
                """.Trim()),
            new("user", source),
        };

        try
        {
            var translated = await _llama.ChatAsync(
                messages,
                temperature: 0.25,
                topP: 0.9,
                maxTokens: Math.Min(_opt.MaxOutputTokens, 1200),
                useReasoning: false,
                ct: ct);

            return string.IsNullOrWhiteSpace(translated) ? null : translated.Trim();
        }
        catch
        {
            return null;
        }
    }
}
