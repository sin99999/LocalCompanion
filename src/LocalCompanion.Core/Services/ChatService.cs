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
    bool ContinueSession = false);

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
    private readonly LlamaOptions _opt;

    public ChatService(
        LlamaServerClient llama,
        RagService rag,
        RagDatabase db,
        CharacterRepository character,
        CharacterPresetService presets,
        ModelCatalogService models,
        AppSettingsStore appSettings,
        IOptions<LlamaOptions> opt)
    {
        _llama = llama;
        _rag = rag;
        _db = db;
        _character = character;
        _presets = presets;
        _models = models;
        _appSettings = appSettings;
        _opt = opt.Value;
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

        MarkSessionClosed(sessionId);
    }

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

    private readonly record struct HistoryMode(bool Load, bool Save, string? PresetKey, string? SessionId);

    private HistoryMode ResolveHistory(ChatRequestDto req)
    {
        var sessionKey = CharacterPresetService.ResolveSessionPresetKey(_presets.GetActivePresetFileName());
        if (string.IsNullOrWhiteSpace(req.SessionId))
            return new(false, false, sessionKey, null);
        if (!req.UseHistory)
            return new(false, false, sessionKey, req.SessionId);

        var session = GetSession(req.SessionId);
        if (session is null)
            return new(false, false, sessionKey, req.SessionId);
        if (!string.Equals(session.PresetKey, sessionKey, StringComparison.OrdinalIgnoreCase))
            return new(false, true, sessionKey, req.SessionId);

        return new(true, true, sessionKey, req.SessionId);
    }

    public async Task<ChatResponseDto> ChatAsync(ChatRequestDto req, CancellationToken ct)
    {
        if (!await _llama.PingAsync(ct))
            throw new InvalidOperationException(LlamaServerClient.ConnectionFailedMessage);

        var profile = _character.Get();
        // 履歴・出力の予算は、実際に起動中の llama-server ctx を優先して計算する。
        var effectiveContext = ResolveEffectiveContext(profile.ContextLength);
        var hasImage = req.ImagesBase64 is { Length: > 0 };
        var hasFile = !string.IsNullOrWhiteSpace(req.AttachedText);
        var heavyRequest = hasImage || hasFile;
        var runtime = await _models.GetRuntimeStatusAsync(_llama, ct);
        var modelFileName = runtime.ActiveModelFileName;
        var japaneseReply = TextScriptHelper.LooksJapanese(req.Message);
        var systemParts = BuildSystemParts(profile, runtime, req.Message, japaneseReply);

        string[]? ragSources = null;
        if (req.UseRag && req.Message.Length >= 4 && !heavyRequest && _rag.GetChunkCount() > 0)
        {
            var hits = await TrySearchRagAsync(req.Message, ct);
            if (hits.Count > 0)
            {
                ragSources = hits.Select((h, i) => h.FormatSourceLabel(i)).ToArray();
                var ragHeader = ChatSystemPromptTexts.RagHitsHeader(japaneseReply);
                systemParts.Add(ragHeader + "\n" + string.Join("\n\n", hits.Select((h, i) => h.FormatForPrompt(i))));
            }
        }
        else if (!req.UseRag)
        {
            systemParts.Add(ChatSystemPromptTexts.RagDisabledNote(japaneseReply));
        }

        var userContent = BuildUserContent(req, _opt.MaxAttachTextChars);
        var systemText = string.Join("\n\n", systemParts);
        var historyMode = ResolveHistory(req);

        var attempts = BuildAttempts(heavyRequest, effectiveContext);
        string? reply = null;
        Exception? lastError = null;
        var usedHistory = false;

        foreach (var attempt in attempts)
        {
            var history = historyMode.Load
                ? LoadSessionHistoryWithinBudget(
                    historyMode.SessionId!,
                    effectiveContext,
                    systemText.Length,
                    userContent.Length,
                    profile.MaxOutputTokens,
                    hasImage,
                    attempt.HistoryBudgetFraction)
                : Array.Empty<ChatTurn>();
            if (history.Count > 0)
                usedHistory = true;
            var messages = new List<ChatTurn> { new("system", systemText) };
            messages.AddRange(history);
            messages.Add(new ChatTurn("user", userContent, req.ImagesBase64));

            var maxOut = ComputeSafeMaxTokens(
                profile.MaxOutputTokens,
                effectiveContext,
                messages,
                hasImage,
                attempt.MaxOutputCap);

            try
            {
                reply = await _llama.ChatAsync(
                    messages,
                    temperature: profile.Temperature,
                    topP: profile.TopP,
                    topK: profile.TopK,
                    maxTokens: maxOut,
                    useReasoning: req.UseReasoning,
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

        if (historyMode.Save
            && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
        {
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(reply, HistorySaveMaxChars));
            TouchSession(historyMode.SessionId, req.Message);
        }

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

        var profile = _character.Get();
        // 履歴・出力の予算は、実際に起動中の llama-server ctx を優先して計算する。
        var effectiveContext = ResolveEffectiveContext(profile.ContextLength);
        var hasImage = req.ImagesBase64 is { Length: > 0 };
        var hasFile = !string.IsNullOrWhiteSpace(req.AttachedText);
        var heavyRequest = hasImage || hasFile;
        var runtime = await _models.GetRuntimeStatusAsync(_llama, ct);
        var modelFileName = runtime.ActiveModelFileName;
        var japaneseReply = TextScriptHelper.LooksJapanese(req.Message);
        var systemParts = BuildSystemParts(profile, runtime, req.Message, japaneseReply);

        string[]? ragSources = null;
        if (req.UseRag && req.Message.Length >= 4 && !heavyRequest && _rag.GetChunkCount() > 0)
        {
            var hits = await TrySearchRagAsync(req.Message, ct);
            if (hits.Count > 0)
            {
                ragSources = hits.Select((h, i) => h.FormatSourceLabel(i)).ToArray();
                var ragHeader = ChatSystemPromptTexts.RagHitsHeader(japaneseReply);
                systemParts.Add(ragHeader + "\n" + string.Join("\n\n", hits.Select((h, i) => h.FormatForPrompt(i))));
            }
        }
        else if (!req.UseRag)
        {
            systemParts.Add(ChatSystemPromptTexts.RagDisabledNote(japaneseReply));
        }

        var userContent = BuildUserContent(req, _opt.MaxAttachTextChars);
        var systemText = string.Join("\n\n", systemParts);
        var historyMode = ResolveHistory(req);
        var attempts = BuildAttempts(heavyRequest, effectiveContext);

        var replyBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var maxReplyChars = _opt.MaxReplyChars;
        var streamTruncated = false;
        var usedHistory = false;
        Exception? lastStreamError = null;
        var streamed = false;

        foreach (var attempt in attempts)
        {
            replyBuilder.Clear();
            reasoningBuilder.Clear();
            streamTruncated = false;

            var history = historyMode.Load
                ? LoadSessionHistoryWithinBudget(
                    historyMode.SessionId!,
                    effectiveContext,
                    systemText.Length,
                    userContent.Length,
                    profile.MaxOutputTokens,
                    hasImage,
                    attempt.HistoryBudgetFraction)
                : Array.Empty<ChatTurn>();
            usedHistory = history.Count > 0;
            var messages = new List<ChatTurn> { new("system", systemText) };
            messages.AddRange(history);
            messages.Add(new ChatTurn("user", userContent, req.ImagesBase64));
            var outputCap = req.UseReasoning
                ? Math.Min(attempt.MaxOutputCap, 4096)
                : attempt.MaxOutputCap;
            var maxOut = ComputeSafeMaxTokens(
                profile.MaxOutputTokens,
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
                useReasoning: req.UseReasoning,
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

                if (!hasNext)
                {
                    streamed = true;
                    break;
                }

                var piece = streamEnumerator.Current;
                if (piece.Kind == "reasoning")
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

        if (historyMode.Save
            && !string.IsNullOrWhiteSpace(historyMode.SessionId)
            && !string.IsNullOrWhiteSpace(historyMode.PresetKey))
        {
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "user", SummarizeForHistory(req));
            var historyReply = !string.IsNullOrWhiteSpace(reply) ? reply : reasoning;
            SaveMessage(historyMode.SessionId, historyMode.PresetKey, "assistant", TruncateContent(historyReply, HistorySaveMaxChars));
            TouchSession(historyMode.SessionId, req.Message);
        }

        var meta = formatMetaForStream(ragSources is { Length: > 0 }, req.UseReasoning, usedHistory, hasFile, modelFileName, runtime);
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
        bool usedAttachment,
        string? modelFileName,
        ModelRuntimeStatus runtime)
    {
        var loc = LocalizationService.Instance;
        var parts = new List<string>();
        if (runtime.ModelMismatch) parts.Add(loc.Get("Chat.Meta.ModelMismatch"));
        if (usedRag) parts.Add(loc.Get("Chat.Meta.Rag"));
        if (usedReasoning) parts.Add(loc.Get("Chat.Meta.ReasoningOn"));
        if (usedAttachment) parts.Add(loc.Get("Chat.Meta.Attachment"));
        if (usedHistory) parts.Add(loc.Get("Chat.Meta.History"));
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

    private List<string> BuildSystemParts(
        CharacterProfileDto profile,
        ModelRuntimeStatus runtime,
        string userMessage,
        bool japaneseReply)
    {
        var isDefaultCharacter = CharacterPresetService.IsNoneSelection(_presets.GetActivePresetFileName());
        var parts = new List<string>();

        var userDisplayName = _appSettings.Load().UserDisplayName?.Trim();
        if (!string.IsNullOrEmpty(userDisplayName))
            parts.Add(ChatSystemPromptTexts.UserNameLine(userDisplayName, japaneseReply));

        parts.Add(isDefaultCharacter
            ? ChatSystemPromptTexts.DefaultLanguageInstruction(japaneseReply)
            : ChatSystemPromptTexts.CharacterLanguageInstruction(japaneseReply));

        if (!isDefaultCharacter)
        {
            if (!string.IsNullOrWhiteSpace(profile.Persona))
                parts.Add(profile.Persona.Trim());
            if (!string.IsNullOrWhiteSpace(profile.SpeakingStyle))
                parts.Add(ChatSystemPromptTexts.SpeakingStyleLine(profile.SpeakingStyle.Trim(), japaneseReply));
            if (!string.IsNullOrWhiteSpace(profile.Name))
                parts.Add(ChatSystemPromptTexts.CharacterNameLine(profile.Name.Trim(), japaneseReply));
            if (!string.IsNullOrEmpty(userDisplayName) && !string.IsNullOrWhiteSpace(profile.Name))
            {
                parts.Add(ChatSystemPromptTexts.UserAndCharacterNameDistinction(
                    userDisplayName,
                    profile.Name.Trim(),
                    japaneseReply));
            }
        }

        parts.Add(ChatSystemPromptTexts.ReadabilityInstruction(japaneseReply));
        parts.Add(ChatReplyLimitHelper.SystemLimitNote(_opt.MaxReplyChars, japaneseReply));
        if (ChatReplyLimitHelper.UserRequestsExcessiveLength(userMessage, _opt.MaxReplyChars))
            parts.Add(ChatReplyLimitHelper.ExcessiveRequestNote(_opt.MaxReplyChars, japaneseReply));

        if (!string.IsNullOrWhiteSpace(runtime.LoadedModelFileName))
            parts.Add(ChatSystemPromptTexts.LoadedModelLine(runtime.LoadedModelFileName, japaneseReply));
        else if (!string.IsNullOrWhiteSpace(runtime.SelectedModelFileName))
            parts.Add(ChatSystemPromptTexts.SelectedModelLine(runtime.SelectedModelFileName, japaneseReply));

        if (runtime.ModelMismatch)
        {
            parts.Add(ChatSystemPromptTexts.ModelMismatchLine(
                runtime.SelectedModelFileName ?? "",
                runtime.LoadedModelFileName ?? "",
                japaneseReply));
        }

        if (!string.IsNullOrWhiteSpace(runtime.MmprojWarning))
            parts.Add(runtime.MmprojWarning);

        parts.Add(ChatSystemPromptTexts.MemoryDistinction(japaneseReply));
        parts.Add(ChatSystemPromptTexts.AttachmentInstruction(japaneseReply));
        parts.Add(ChatSystemPromptTexts.ImageInstruction(japaneseReply));
        parts.Add(ChatInputLanguageDirective.Build(userMessage));
        return parts;
    }

    private static string BuildUserContent(ChatRequestDto req, int maxAttachChars)
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
            parts.Add("（画像が添付されています。描写と、写っている文字の読み取り（OCR）の両方を答えてください）");
        parts.Add(req.Message);
        return string.Join("\n\n", parts);
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
    }

    private List<(string Role, string Content)> LoadRecentSessionRows(string sessionId, int maxRows)
    {
        if (maxRows <= 0 || string.IsNullOrWhiteSpace(sessionId))
            return [];

        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT role, content FROM chat_messages
            WHERE session_id = $s
            ORDER BY id DESC LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$n", maxRows);

        var rows = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1)));
        return rows;
    }

    /// <summary>セッション内の直近メッセージを、コンテキスト余力の範囲で古い順に返します。</summary>
    private IReadOnlyList<ChatTurn> LoadSessionHistoryWithinBudget(
        string sessionId,
        int contextLength,
        int systemCharCount,
        int currentUserCharCount,
        int reservedOutputTokens,
        bool currentMessageHasImage,
        double budgetFraction)
    {
        if (budgetFraction <= 0 || string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<ChatTurn>();

        var imageOverhead = currentMessageHasImage ? 7500 : 0;
        var fixedChars = systemCharCount + currentUserCharCount + imageOverhead;
        var fixedTokens = fixedChars / 2 + 384;
        var outputReserve = Math.Clamp(reservedOutputTokens, 256, 8192);
        var totalBudget = contextLength - outputReserve - 128;
        var historyTokenBudget = (int)((totalBudget - fixedTokens) * budgetFraction);
        if (historyTokenBudget < 128)
            return Array.Empty<ChatTurn>();

        var historyCharBudget = Math.Max(256, historyTokenBudget * 2);
        var rows = LoadRecentSessionRows(sessionId, MaxHistoryMessagesToScan);
        if (rows.Count == 0)
            return Array.Empty<ChatTurn>();

        var selected = new List<ChatTurn>();
        var usedChars = 0;
        foreach (var (role, content) in rows)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var piece = content;
            var pieceLen = piece.Length;
            if (usedChars + pieceLen > historyCharBudget)
            {
                if (selected.Count == 0)
                {
                    piece = TruncateContent(piece, historyCharBudget);
                    pieceLen = piece.Length;
                }
                else
                {
                    break;
                }
            }

            selected.Add(new ChatTurn(role, piece));
            usedChars += pieceLen;
            if (usedChars >= historyCharBudget)
                break;
        }

        selected.Reverse();
        while (selected.Count > 0
               && string.Equals(selected[0].Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            selected.RemoveAt(0);
        }

        return selected;
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
            return Array.Empty<RagSearchHit>();
        }
        catch (Exception)
        {
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
