using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagTextChunkerTests
{
    [Fact]
    public void ChunkText_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(RagTextChunker.ChunkText("", 900, 128));
        Assert.Empty(RagTextChunker.ChunkText("   ", 900, 128));
    }

    [Fact]
    public void ChunkText_ShortParagraph_StaysSingleChunk()
    {
        var text = "短い段落です。";
        var chunks = RagTextChunker.ChunkText(text, 900, 128);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void SplitOversized_PrefersSentenceBreak()
    {
        var sentence = new string('あ', 200) + "。";
        var text = sentence + sentence + sentence;
        var chunks = RagTextChunker.SplitOversized(text, 250, 32).ToList();

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Length <= 250));
    }

    [Fact]
    public void ChunkText_LargeParagraph_ProducesMultipleChunksWithOverlap()
    {
        var paragraph = string.Join("", Enumerable.Repeat("文です。", 200));
        var chunks = RagTextChunker.ChunkText(paragraph, 400, 64);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 400));
    }
}
