using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagFormalLegalCueTests
{
    [Theory]
    [InlineData("贈賄の罰則は？", true)]
    [InlineData("第8条 全文", true)]
    [InlineData("14歳だけど同意してても捕まっちゃう？w", false)]
    [InlineData("副業100億オファー相談", false)]
    public void IsFormalLegalQuery_DetectsStrictLookup(string query, bool expected)
    {
        Assert.Equal(expected, RagFormalLegalCue.IsFormalLegalQuery(query));
    }
}
