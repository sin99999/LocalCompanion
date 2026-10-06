using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class JuliaRoutePolicyTests
{
    [Theory]
    [InlineData("ウェブで最新の為替を調べて", null, false)]
    [InlineData("刑法235条について調べて", null, false)]
    [InlineData("こんにちは", null, false)]
    [InlineData("量子コンピュータについて調べて", null, true)]
    [InlineData("それを調べて", "刑法235条は？", false)]
    [InlineData("それを調べて", "今日の東京の天気は？", true)]
    [InlineData("https://example.com を見て", null, false)]
    [InlineData("残業代はどうなる", null, false)]
    [InlineData("インターネット回線が遅い", null, false)]
    [InlineData("ネットワークが切れた", null, false)]
    [InlineData("最新情報の刑法4条を調べて", null, false)]
    [InlineData("登録資料を検索して第11条を出して", null, false)]
    [InlineData("就業規則を調べて", null, false)]
    [InlineData("ウェブで刑法235条を調べて", null, false)]
    [InlineData("万引きは違法？", null, false)]
    public void ShouldAsk_LeavesExplicitWebAndLocalNotesAlone(
        string message,
        string? previous,
        bool expected)
    {
        Assert.Equal(expected, JuliaRoutePolicy.ShouldAsk(message, previous));
    }

    [Fact]
    public void ConfidentWeb_TurnsWebOn_AndDoesNotDeepenNotes()
    {
        var choice = JuliaRoutePolicy.ChoiceFromLabel("web", 0.70);
        Assert.Equal(JuliaRouteChoice.Web, choice);
        Assert.True(JuliaRoutePolicy.WebOverride(choice, asked: true));
        Assert.Equal(
            RagConversationMode.Probe,
            JuliaRoutePolicy.ApplyToNotes(RagConversationMode.SoftTopic, choice, asked: true));
    }

    [Fact]
    public void ConfidentNotes_StaysOffTheWeb_AndSearchesSavedNotes()
    {
        var choice = JuliaRoutePolicy.ChoiceFromLabel("notes", 0.91);
        Assert.False(JuliaRoutePolicy.WebOverride(choice, asked: true));
        Assert.Equal(
            RagConversationMode.SoftTopic,
            JuliaRoutePolicy.ApplyToNotes(RagConversationMode.Probe, choice, asked: true));
    }

    [Fact]
    public void LowConfidenceOrUnread_KeepsTheCueWords()
    {
        var low = JuliaRoutePolicy.ChoiceFromLabel("web", 0.69);
        var unread = JuliaRoutePolicy.ChoiceFromLabel(null, 0);
        Assert.Equal(JuliaRouteChoice.Keep, low);
        Assert.Equal(JuliaRouteChoice.Keep, unread);
        Assert.Null(JuliaRoutePolicy.WebOverride(low, asked: true));
        Assert.Equal(
            RagConversationMode.SoftTopic,
            JuliaRoutePolicy.ApplyToNotes(RagConversationMode.SoftTopic, unread, asked: true));
    }

    [Fact]
    public void Chat_SuppressesWeb_AndDoesNotDeepenNotes()
    {
        var choice = JuliaRoutePolicy.ChoiceFromLabel("chat", 0.88);
        Assert.False(JuliaRoutePolicy.WebOverride(choice, asked: true));
        Assert.Equal(
            RagConversationMode.Probe,
            JuliaRoutePolicy.ApplyToNotes(RagConversationMode.SoftTopic, choice, asked: true));
    }

    [Fact]
    public void ExplicitPath_DoesNotApplyAChoice()
    {
        var choice = JuliaRoutePolicy.ChoiceFromLabel("web", 0.99);
        Assert.Null(JuliaRoutePolicy.WebOverride(choice, asked: false));
        Assert.Equal(
            RagConversationMode.Structured,
            JuliaRoutePolicy.ApplyToNotes(RagConversationMode.Structured, choice, asked: false));
    }
}
