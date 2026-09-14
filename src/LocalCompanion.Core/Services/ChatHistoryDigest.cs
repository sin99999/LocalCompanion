namespace LocalCompanion.Services;

internal readonly record struct ChatHistoryRow(long Id, string Role, string Content);

/// <summary>コンテキスト余力に入る直近と、落ちた古い発言。</summary>
internal sealed class ChatHistoryBudgetPick
{
    public static readonly ChatHistoryBudgetPick Empty = new([], []);

    public ChatHistoryBudgetPick(IReadOnlyList<ChatTurn> kept, IReadOnlyList<ChatHistoryRow> dropped)
    {
        Kept = kept;
        Dropped = dropped;
    }

    public IReadOnlyList<ChatTurn> Kept { get; }
    public IReadOnlyList<ChatHistoryRow> Dropped { get; }
}

/// <summary>履歴予算の分割。LLM は呼ばない。</summary>
internal static class ChatHistoryDigest
{
    public const int MaxDigestChars = 1500;
    public const int MaxTranscriptChars = 6000;

    public static bool TryAppendPrompt(List<string> systemParts, string? digest, bool japanese)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return false;

        systemParts.Add(ChatSystemPromptTexts.HistoryDigestBlock(digest, japanese));
        systemParts.Add(ChatSystemPromptTexts.HistoryDigestInstruction(japanese));
        return true;
    }

    public static bool IsAllowedPreset(string? presetKey) =>
        MemoryService.SupportsLongTermMemory(presetKey);

    public static bool NeedsRefresh(long uptoId, IReadOnlyList<ChatHistoryRow> dropped)
    {
        if (dropped.Count == 0)
            return false;
        var maxId = dropped[0].Id;
        for (var i = 1; i < dropped.Count; i++)
        {
            if (dropped[i].Id > maxId)
                maxId = dropped[i].Id;
        }

        return maxId > uptoId;
    }

    public static long MaxDroppedId(IReadOnlyList<ChatHistoryRow> dropped)
    {
        if (dropped.Count == 0)
            return 0;
        var maxId = dropped[0].Id;
        for (var i = 1; i < dropped.Count; i++)
        {
            if (dropped[i].Id > maxId)
                maxId = dropped[i].Id;
        }

        return maxId;
    }

    public static ChatHistoryBudgetPick Pick(IReadOnlyList<ChatHistoryRow> newestFirst, int historyCharBudget)
    {
        if (newestFirst.Count == 0)
            return ChatHistoryBudgetPick.Empty;

        var selectedNewestFirst = new List<ChatHistoryRow>();
        var usedChars = 0;
        foreach (var row in newestFirst)
        {
            if (string.IsNullOrWhiteSpace(row.Content))
                continue;

            var piece = row.Content;
            var pieceLen = piece.Length;
            if (usedChars + pieceLen > historyCharBudget)
            {
                if (selectedNewestFirst.Count == 0)
                {
                    piece = Truncate(piece, historyCharBudget);
                    selectedNewestFirst.Add(row with { Content = piece });
                    usedChars += piece.Length;
                }

                break;
            }

            selectedNewestFirst.Add(row);
            usedChars += pieceLen;
            if (usedChars >= historyCharBudget)
                break;
        }

        selectedNewestFirst.Reverse();
        while (selectedNewestFirst.Count > 0
               && string.Equals(selectedNewestFirst[0].Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            selectedNewestFirst.RemoveAt(0);
        }

        var keptIds = new HashSet<long>();
        var kept = new List<ChatTurn>(selectedNewestFirst.Count);
        foreach (var row in selectedNewestFirst)
        {
            keptIds.Add(row.Id);
            kept.Add(new ChatTurn(row.Role, row.Content));
        }

        var dropped = new List<ChatHistoryRow>();
        foreach (var row in newestFirst)
        {
            if (string.IsNullOrWhiteSpace(row.Content) || keptIds.Contains(row.Id))
                continue;
            dropped.Add(row);
        }

        return new ChatHistoryBudgetPick(kept, dropped);
    }

    public static string FormatNewDroppedTranscript(IReadOnlyList<ChatHistoryRow> dropped, long uptoId)
    {
        var rows = new List<ChatHistoryRow>();
        foreach (var row in dropped)
        {
            if (row.Id > uptoId)
                rows.Add(row);
        }

        rows.Sort((a, b) => a.Id.CompareTo(b.Id));
        var sb = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            var line = $"{row.Role}: {row.Content.Trim()}";
            if (sb.Length + line.Length + 1 > MaxTranscriptChars)
                break;
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(line);
        }

        return sb.ToString();
    }

    public static string ClampDigest(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= MaxDigestChars)
            return trimmed;
        return trimmed[..MaxDigestChars];
    }

    private static string Truncate(string content, int maxChars)
    {
        if (content.Length <= maxChars)
            return content;
        return content[..maxChars] + "\n…（履歴省略）";
    }
}
