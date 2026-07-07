using LocalCompanion.Core.Tests.Fixtures;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagPenalCodeIngestTests
{
    [Fact]
    public void CreateChunks_PenalCodeMarkdown_ExtractsArticleSortKeys()
    {
        var text = PenalCodeTestFixtures.BuildMarkdown();

        var docKind = RagDocumentProfileDetector.Detect("刑法.md", text);
        Assert.Equal(RagDocumentKind.Legal, docKind);

        var drafts = RagStructuralChunker.CreateChunks(text, "刑法.md", size: 900, overlap: 128, docKind);
        var withArticle = drafts.Where(d => d.ArticleSortKey > 0).ToList();

        Assert.True(withArticle.Count >= 50, $"Expected many article chunks, got {withArticle.Count} / {drafts.Count}");

        var art8 = withArticle.FirstOrDefault(d => d.ArticleSortKey == 800);
        Assert.NotNull(art8);
        Assert.Contains("他の法令", art8.HeaderText, StringComparison.Ordinal);
        Assert.Contains("この編の規定", art8.Text, StringComparison.Ordinal);
    }
}
