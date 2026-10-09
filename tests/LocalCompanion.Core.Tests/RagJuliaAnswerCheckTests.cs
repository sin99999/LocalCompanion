using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagJuliaAnswerCheckTests
{
    [Fact]
    public void Keep_LeavesTheQuestionWhenJuliaCannotBeRead()
    {
        Assert.True(RagJuliaAnswerCheck.Keep(null));
        Assert.True(RagJuliaAnswerCheck.Keep(0.35));
        Assert.True(RagJuliaAnswerCheck.Keep(0.9));
    }

    [Fact]
    public void Keep_DropsOnlyAClearNo()
    {
        Assert.False(RagJuliaAnswerCheck.Keep(0.34));
        Assert.False(RagJuliaAnswerCheck.Keep(0));
    }

    [Fact]
    public void BuildState_KeepsTheQuestionAheadOfALongPassage()
    {
        var state = RagJuliaAnswerCheck.BuildState(new string('あ', 800), "なぜ薬指は単独で動かせないのか？");

        Assert.StartsWith("Question: なぜ薬指は単独で動かせないのか？", state);
        Assert.True(state.Length <= LayaDecision.MaxStateChars);
    }

    [Fact]
    public void Apply_DropsLowHitsAndKeepsTheOnesNotAsked()
    {
        var hits = new[] { "a", "b", "c" };

        var kept = RagJuliaAnswerCheck.Apply(hits, new double?[] { 0.1, null });

        Assert.Equal(new[] { "b", "c" }, kept);
    }

    [Fact]
    public void Apply_RestoresTheOriginalListWhenEveryAskedHitIsDropped()
    {
        var hits = new[] { "a", "b" };

        var kept = RagJuliaAnswerCheck.Apply(hits, new double?[] { 0.1, 0.0 });

        Assert.Equal(hits, kept);
    }
}
