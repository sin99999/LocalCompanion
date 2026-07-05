using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagAdvisoryQueryParserTests
{
    [Fact]
    public void TryDetect_MultiSourceQuery_ReturnsTrue()
    {
        const string query = "就業規則では副業禁止だけど、税法上は法人化した方がいい？100億のオファー";
        Assert.True(RagAdvisoryQueryParser.TryDetect(query, out var hints));
        Assert.Contains("就業規則", hints);
        Assert.Contains("税法", hints);
    }
}
