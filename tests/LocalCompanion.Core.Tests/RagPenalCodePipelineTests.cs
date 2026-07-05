using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>IngestTextAsync と同じ前処理→chunk 経路を llama なしで検証する。</summary>
public sealed class RagPenalCodePipelineTests
{
    [Fact]
    public void FullPreIngestPipeline_PenalCodeMd_ExtractsArticles()
    {
        var path = @"C:\Users\SIN\Desktop\PDF\刑法.md";
        Assert.True(File.Exists(path), "刑法.md not found on this machine.");

        var source = path;
        var text = File.ReadAllText(path);
        var options = new RagIngestOptions(
            UseHtmlMarkdown: true,
            UseLlmStructurer: true,
            SaveStructurerCache: true,
            UsePdfLayoutReader: false);

        text = RagIngestPreprocessor.Preprocess(source, text, options);
        text = RagDocumentNormalizer.Normalize(text);
        var docKind = RagDocumentProfileDetector.Detect(source, text);
        Assert.Equal(RagDocumentKind.Legal, docKind);

        // .md は structurer をスキップ（拡張子 .md）
        var ext = Path.GetExtension(source).ToLowerInvariant();
        Assert.True(ext is ".md" or ".markdown");

        text = RagDocumentNormalizer.Normalize(text);
        var drafts = RagStructuralChunker.CreateChunks(text, source, size: 900, overlap: 128, docKind);
        var articleCount = drafts.Count(d => d.ArticleSortKey > 0);

        Assert.True(articleCount >= 50, $"Expected 50+ article chunks, got {articleCount} / {drafts.Count}");

        var art54 = drafts.FirstOrDefault(d => d.ArticleSortKey == 5400);
        Assert.NotNull(art54);
        Assert.Contains("一個の行為", art54!.HeaderText, StringComparison.Ordinal);
    }
}
