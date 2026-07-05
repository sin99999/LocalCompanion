using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSourceCatalogQueryParserTests
{
    [Theory]
    [InlineData("RAGに何が登録されてる？")]
    [InlineData("資料一覧を教えて")]
    [InlineData("どんなファイルが取り込まれている？")]
    public void TryDetect_MatchesCatalogQueries(string query)
    {
        Assert.True(RagSourceCatalogQueryParser.TryDetect(query));
    }

    [Theory]
    [InlineData("刑法第8条全文")]
    [InlineData("賃金の支払いはいつ")]
    public void TryDetect_ReturnsFalseForNonCatalogQueries(string query)
    {
        Assert.False(RagSourceCatalogQueryParser.TryDetect(query));
    }

    [Fact]
    public void Plan_SourceCatalog_UsesVerbatimMode()
    {
        var plan = RagQueryPlanner.Plan("RAGに何が登録されてる？", previousUserMessage: null);
        Assert.Equal(RagQueryIntent.SourceCatalog, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
    }
}
