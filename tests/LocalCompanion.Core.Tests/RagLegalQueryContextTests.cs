using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagLegalQueryContextTests
{
    [Theory]
    [InlineData("刑法第54条全文", true)]
    [InlineData("国外犯ってどうなる？", true)]
    [InlineData("労基法の8条", true)]
    [InlineData("4条って何？", true)]
    [InlineData("READMEの3条", false)]
    [InlineData("vectorの3条項", false)]
    public void LooksLikeLegalArticleQuery_DistinguishesLegalContext(string query, bool expected)
    {
        Assert.Equal(expected, RagLegalQueryContext.LooksLikeLegalArticleQuery(query, sourceHint: null));
    }

    [Fact]
    public void Plan_NonLegalArticleNumber_UsesGeneralNotArticle()
    {
        var plan = RagQueryPlanner.Plan("READMEの第3条について教えて", previousUserMessage: null);
        Assert.NotEqual(RagQueryIntent.Article, plan.Intent);
    }

    [Fact]
    public void Plan_CppDefinition_UsesDefinitionIntent()
    {
        var plan = RagQueryPlanner.Plan("vectorの使い方", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.Definition, plan.Intent);
        Assert.Equal("vector", plan.TopicKeyword);
    }
}
