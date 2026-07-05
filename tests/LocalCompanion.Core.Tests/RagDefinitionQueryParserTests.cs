using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagDefinitionQueryParserTests
{
    [Theory]
    [InlineData("FTLとは", "FTL")]
    [InlineData("贈賄の意味は？", "贈賄")]
    [InlineData("what is RAG?", "RAG")]
    public void TryGetTerm_MatchesDefinitionQuestions(string query, string expected)
    {
        Assert.True(RagDefinitionQueryParser.TryGetTerm(query, out var term));
        Assert.Equal(expected, term);
    }

    [Fact]
    public void TryGetTerm_RejectsLegalArticleQuery()
    {
        Assert.False(RagDefinitionQueryParser.TryGetTerm("第8条とは", out _));
    }
}
