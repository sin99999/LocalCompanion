using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class LegalFieldExtractorTests
{
    [Fact]
    public void ParseArticle_FallsBackToBodyLeadWhenHeaderEmpty()
    {
        var body = """
            #### 第8条（他の法令の罪に対する適用）

            この編の規定は、他の法令の罪についても、適用する。
            """;

        var (_, _, sortKey) = LegalFieldExtractor.ParseArticle("", body);
        Assert.Equal(800L, sortKey);
    }
}
