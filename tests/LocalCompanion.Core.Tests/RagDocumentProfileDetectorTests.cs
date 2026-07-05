using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagDocumentProfileDetectorTests
{
    [Fact]
    public void Detect_LegalFilename_ReturnsLegal()
    {
        var text = "第1条\nテスト\n第2条\n第3条\n懲役";
        Assert.Equal(RagDocumentKind.Legal, RagDocumentProfileDetector.Detect("刑法.md", text));
    }

    [Fact]
    public void Detect_GlossaryFilename_ReturnsGlossary()
    {
        Assert.Equal(RagDocumentKind.Glossary, RagDocumentProfileDetector.Detect("用語集.md", "## A\n\n## B\n\n"));
    }
}
