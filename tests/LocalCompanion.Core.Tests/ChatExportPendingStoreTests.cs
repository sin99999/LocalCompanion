using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportPendingStoreTests
{
    [Fact]
    public void TryResolveConflictContinuation_ReturnsStoredDocumentWithOverwritePolicy()
    {
        var store = new ChatExportPendingStore();
        var request = new ChatExportRequest(
            "刑法を調べて",
            null,
            ".txt",
            new ChatExportTarget(ChatExportTargetKind.Desktop),
            ChatExportConflictPolicy.AskUser);
        var document = new ChatExportDocument("刑法メモ", "本文");
        store.Set("session-1", request, document, "チャット回答", ["刑法.md"]);

        var ok = store.TryResolveConflictContinuation("session-1", "上書き保存", out var pending, out var policy);

        Assert.True(ok);
        Assert.Equal(ChatExportConflictPolicy.Overwrite, policy);
        Assert.Equal("刑法メモ", pending.Document.Title);
        Assert.Equal("チャット回答", pending.CleanReply);
    }

    [Fact]
    public void TryResolveConflictContinuation_RejectsCasualNamingQuestion()
    {
        var store = new ChatExportPendingStore();
        store.Set(
            "session-1",
            new ChatExportRequest("q", null, ".txt", new ChatExportTarget(ChatExportTargetKind.Desktop)),
            new ChatExportDocument("t", "b"),
            "reply",
            null);

        Assert.False(store.TryResolveConflictContinuation(
            "session-1",
            "この概念に名前を付けて説明して",
            out _,
            out _));
    }

    [Fact]
    public void Clear_RemovesPendingEntry()
    {
        var store = new ChatExportPendingStore();
        store.Set(
            "session-1",
            new ChatExportRequest("q", null, ".txt", new ChatExportTarget(ChatExportTargetKind.Desktop)),
            new ChatExportDocument("t", "b"),
            "reply",
            null);

        store.Clear("session-1");

        Assert.False(store.TryResolveConflictContinuation("session-1", "上書き保存", out _, out _));
    }
}
