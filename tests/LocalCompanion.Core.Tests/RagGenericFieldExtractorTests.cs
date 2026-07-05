using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagGenericFieldExtractorTests
{
    [Fact]
    public void Enrich_MarkdownHeading_ExtractsDefinitionLead()
    {
        var (entryKey, definitionLead, chunkKind, _) = RagGenericFieldExtractor.Enrich(
            "### FTL",
            " Faster Than Light の略。光速より速い移動。",
            "",
            3,
            "",
            "",
            "",
            RagDocumentKind.Glossary);

        Assert.Equal("ftl", entryKey);
        Assert.Contains("光速", definitionLead);
        Assert.Equal("glossary", chunkKind);
    }

    [Fact]
    public void Enrich_InlineTermDefinition_ParsesEntryKey()
    {
        var (entryKey, definitionLead, chunkKind, _) = RagGenericFieldExtractor.Enrich(
            "**贈賄** — 公務員に対する不正な利益供与",
            "",
            "",
            0,
            "",
            "",
            "",
            RagDocumentKind.Glossary);

        Assert.Equal("贈賄", entryKey);
        Assert.Contains("公務員", definitionLead);
        Assert.Equal("definition", chunkKind);
    }
}
