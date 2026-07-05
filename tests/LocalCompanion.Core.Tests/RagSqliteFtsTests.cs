using LocalCompanion.Data;

namespace LocalCompanion.Core.Tests;

public sealed class RagSqliteFtsTests
{
    [Fact]
    public void BuildMatchQuery_JoinTokensWithOr()
    {
        var query = RagSqliteFts.BuildMatchQuery("労基法 第8条 全文");
        Assert.Contains("労基法", query);
        Assert.Contains(" OR ", query);
    }

    [Fact]
    public void BuildMatchQuery_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", RagSqliteFts.BuildMatchQuery("   "));
    }

    [Fact]
    public void BuildMatchQuery_StripsUnsafeCharactersFromTokens()
    {
        var query = RagSqliteFts.BuildMatchQuery("test\"(foo)*");
        Assert.Contains("test", query);
        Assert.Contains("foo", query);
        Assert.DoesNotContain("*", query);
    }
}
