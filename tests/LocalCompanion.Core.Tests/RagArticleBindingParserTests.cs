using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagArticleBindingParserTests
{
    [Fact]
    public void Parse_PairsEachStatuteWithItsArticle()
    {
        var bindings = RagArticleBindingParser.Parse("刑法4条と民法104条");
        Assert.Equal(2, bindings.Count);
        Assert.Equal("刑法", bindings[0].Hint);
        Assert.Equal(400, bindings[0].SortKey);
        Assert.Equal("民法", bindings[1].Hint);
        Assert.Equal(10400, bindings[1].SortKey);
    }

    [Fact]
    public void Parse_InheritsStatuteForSecondNumber()
    {
        var bindings = RagArticleBindingParser.Parse("刑法4条と104条");
        Assert.Equal(2, bindings.Count);
        Assert.Equal("刑法", bindings[0].Hint);
        Assert.Equal("刑法", bindings[1].Hint);
        Assert.Equal(400, bindings[0].SortKey);
        Assert.Equal(10400, bindings[1].SortKey);
    }

    [Fact]
    public void Parse_BareArticle_HasNoBindings()
    {
        Assert.Empty(RagArticleBindingParser.Parse("4条って何？"));
    }
}
