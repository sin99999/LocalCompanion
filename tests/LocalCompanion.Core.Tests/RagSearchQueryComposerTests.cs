using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSearchQueryComposerTests
{
    [Fact]
    public void Compose_FollowUpQuestion_MergesPreviousUserMessage()
    {
        var query = RagSearchQueryComposer.Compose(
            "RAGを参照して正確に教えて？",
            "贈賄の罰則は？");

        Assert.Contains("贈賄", query);
        Assert.Contains("RAG", query);
    }

    [Fact]
    public void Compose_SubstantiveQuestion_KeepsCurrentOnly()
    {
        var query = RagSearchQueryComposer.Compose(
            "刑法の第1条全文を教えて",
            "贈賄の罰則は？");

        Assert.Equal("刑法の第1条全文を教えて", query);
    }
}
