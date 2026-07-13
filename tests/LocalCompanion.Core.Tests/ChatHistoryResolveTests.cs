using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatHistoryResolveTests
{
    [Fact]
    public void ResolveHistoryCore_PresetMismatch_DoesNotLoadOrSave()
    {
        var mode = ChatService.ResolveHistoryCore(
            activeSessionKey: "char-b.json",
            requestSessionId: "session-1",
            useHistory: true,
            sessionPresetKey: "char-a.json");

        Assert.False(mode.Load);
        Assert.False(mode.Save);
        Assert.Equal("char-b.json", mode.PresetKey);
        Assert.Equal("session-1", mode.SessionId);
    }

    [Fact]
    public void ResolveHistoryCore_MatchingPreset_LoadsAndSaves()
    {
        var mode = ChatService.ResolveHistoryCore(
            activeSessionKey: "char-a.json",
            requestSessionId: "session-1",
            useHistory: true,
            sessionPresetKey: "char-a.json");

        Assert.True(mode.Load);
        Assert.True(mode.Save);
    }

    [Fact]
    public void ResolveHistoryCore_HistoryOff_DoesNotLoadOrSave()
    {
        var mode = ChatService.ResolveHistoryCore(
            activeSessionKey: "char-a.json",
            requestSessionId: "session-1",
            useHistory: false,
            sessionPresetKey: "char-a.json");

        Assert.False(mode.Load);
        Assert.False(mode.Save);
    }

    [Fact]
    public void ResolveHistoryCore_NullOrBlankSessionId_DoesNotLoadOrSave()
    {
        var blank = ChatService.ResolveHistoryCore(
            activeSessionKey: "char-a.json",
            requestSessionId: "   ",
            useHistory: true,
            sessionPresetKey: "char-a.json");

        Assert.False(blank.Load);
        Assert.False(blank.Save);
        Assert.Null(blank.SessionId);

        var missing = ChatService.ResolveHistoryCore(
            activeSessionKey: "char-a.json",
            requestSessionId: null,
            useHistory: true,
            sessionPresetKey: "char-a.json");

        Assert.False(missing.Load);
        Assert.False(missing.Save);
        Assert.Null(missing.SessionId);
    }
}
