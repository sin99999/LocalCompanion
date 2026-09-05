using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatReasoningDisplayTests
{
    [Fact]
    public void ShowPanel_LiveBeforeBody_IsTrue()
    {
        Assert.True(ChatReasoningDisplay.ShowPanel(liveStream: true, bodyText: null));
        Assert.True(ChatReasoningDisplay.ShowPanel(liveStream: true, bodyText: ""));
    }

    [Fact]
    public void ShowPanel_LiveAfterBodyStarted_IsFalse()
    {
        Assert.False(ChatReasoningDisplay.ShowPanel(liveStream: true, bodyText: "最終稿"));
    }

    [Fact]
    public void ShowPanel_Completed_IsFalseEvenIfReasoningExists()
    {
        Assert.False(ChatReasoningDisplay.ShowPanel(liveStream: false, bodyText: "最終稿"));
        Assert.False(ChatReasoningDisplay.ShowPanel(liveStream: false, bodyText: ""));
    }
}
