using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>上書き確認待ちのエクスポート内容をセッション単位で保持する。</summary>
internal sealed class ChatExportPendingStore
{
    private readonly Dictionary<string, PendingChatExport> _bySession = new(StringComparer.Ordinal);

    public void Set(
        string sessionId,
        ChatExportRequest request,
        ChatExportDocument document,
        string cleanReply,
        string[]? ragSources)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        _bySession[sessionId] = new PendingChatExport(request, document, cleanReply, ragSources);
    }

    public void Clear(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        _bySession.Remove(sessionId);
    }

    public bool TryResolveConflictContinuation(
        string? sessionId,
        string message,
        out PendingChatExport pending,
        out ChatExportConflictPolicy policy)
    {
        policy = ChatExportConflictPolicy.AskUser;
        if (string.IsNullOrWhiteSpace(sessionId)
            || !_bySession.TryGetValue(sessionId, out var stored))
        {
            pending = default!;
            return false;
        }

        pending = stored;

        if (!ChatExportRequestParser.TryParseConflictResolution(message, out policy))
            return false;

        return true;
    }
}

internal sealed record PendingChatExport(
    ChatExportRequest Request,
    ChatExportDocument Document,
    string CleanReply,
    string[]? RagSources);
