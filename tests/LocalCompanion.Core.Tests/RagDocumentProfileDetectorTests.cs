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

    [Fact]
    public void Detect_CppReferenceFilename_ReturnsGeneral()
    {
        Assert.Equal(RagDocumentKind.General, RagDocumentProfileDetector.Detect("cpp-reference.md", "# vector\n\ntext"));
    }

    [Fact]
    public void Detect_FinanceGlossaryFilename_ReturnsGlossary()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 6).Select(i => $"## Term{i}\n\nDefinition line for term {i}."));
        Assert.Equal(RagDocumentKind.Glossary, RagDocumentProfileDetector.Detect("金融用語集.md", text));
    }
}
