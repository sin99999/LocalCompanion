namespace LocalCompanion.Services;

internal interface IChatHistoryDigestLlm
{
    Task<string?> SummarizeAsync(string existingDigest, string droppedTranscript, CancellationToken ct);
}

internal sealed class LlamaChatHistoryDigestLlm : IChatHistoryDigestLlm
{
    private readonly LlamaServerClient _llama;

    public LlamaChatHistoryDigestLlm(LlamaServerClient llama) => _llama = llama;

    public async Task<string?> SummarizeAsync(string existingDigest, string droppedTranscript, CancellationToken ct)
    {
        var existing = string.IsNullOrWhiteSpace(existingDigest)
            ? "(none)"
            : existingDigest.Trim();
        var prompt = new List<ChatTurn>
        {
            new(
                "system",
                """
                Update a short running summary of older chat turns that no longer fit in context.
                Keep names, decisions, and facts. Do not invent.
                Write plain text only. No headings, bullets, or quotes around the whole summary.
                Use the same language as the dropped turns. Max 1500 characters.
                """.Trim()),
            new(
                "user",
                $"Existing summary:\n{existing}\n\nNewly dropped turns:\n{droppedTranscript}"),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var raw = await _llama.ChatAsync(
            prompt,
            temperature: 0.2,
            topP: 0.9,
            maxTokens: 700,
            useReasoning: false,
            ct: timeout.Token);
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}

/// <summary>溢れ境界が進んだときだけ要約を更新する。失敗したら列は触らない。</summary>
internal sealed class ChatHistoryDigestWriter
{
    private readonly ChatHistoryDigestStore _store;
    private readonly IChatHistoryDigestLlm _llm;

    public ChatHistoryDigestWriter(ChatHistoryDigestStore store, IChatHistoryDigestLlm llm)
    {
        _store = store;
        _llm = llm;
    }

    public async Task TryUpdateAsync(
        string sessionId,
        IReadOnlyList<ChatHistoryRow> dropped,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || dropped.Count == 0)
            return;

        var (existing, uptoId) = _store.Read(sessionId);
        if (!ChatHistoryDigest.NeedsRefresh(uptoId, dropped))
            return;

        var transcript = ChatHistoryDigest.FormatNewDroppedTranscript(dropped, uptoId);
        if (string.IsNullOrWhiteSpace(transcript))
            return;

        string? raw;
        try
        {
            raw = await _llm.SummarizeAsync(existing, transcript, ct).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return;

        var digest = ChatHistoryDigest.ClampDigest(raw);
        if (digest.Length == 0)
            return;

        _store.Write(sessionId, digest, ChatHistoryDigest.MaxDroppedId(dropped));
    }
}
