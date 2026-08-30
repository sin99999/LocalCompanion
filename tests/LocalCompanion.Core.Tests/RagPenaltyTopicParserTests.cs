using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagPenaltyTopicParserTests
{
    [Theory]
    [InlineData("贈賄の罰則は？", "贈賄")]
    [InlineData("殺人の罰則", "殺人")]
    [InlineData("住居侵入罪の罰則は？", "住居侵入")]
    public void TryGetTopicKeyword_ParsesPenaltyQuestions(string query, string expected)
    {
        Assert.True(RagPenaltyTopicParser.TryGetTopicKeyword(query, out var keyword));
        Assert.Equal(expected, keyword);
    }

    [Fact]
    public void BuildTextPatterns_ForBriberyOffer_IncludesSupplyPhrase()
    {
        var patterns = RagPenaltyTopicParser.BuildTextPatterns("贈賄");
        Assert.Contains("贈賄", patterns);
        Assert.Contains("賄賂を供与", patterns);
    }
}
