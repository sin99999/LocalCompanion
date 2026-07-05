using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagGenericFieldExtractorFaqTests
{
    [Fact]
    public void TryParseFaqPair_ExtractsQuestionAndAnswer()
    {
        Assert.True(RagGenericFieldExtractor.TryParseFaqPair(
            "Q: 有給休暇の取得方法",
            "A: 事前に上司へ申請し、承認後に取得できます。",
            out var q,
            out var a));
        Assert.Contains("有給", q);
        Assert.Contains("申請", a);
    }

    [Fact]
    public void Enrich_SetsFaqMetadata()
    {
        var (entryKey, definitionLead, chunkKind, _) = RagGenericFieldExtractor.Enrich(
            "Q: テレワークのルール",
            "A: 週2日まで可能です。",
            "",
            2,
            "",
            "",
            "",
            RagDocumentKind.General);
        Assert.Equal("faq", chunkKind);
        Assert.NotEmpty(entryKey);
        Assert.Contains("週2", definitionLead);
    }
}
