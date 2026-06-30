using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagStructuralChunkerTests
{
    [Fact]
    public void CreateChunks_MarkdownHeadings_SplitBySection()
    {
        var text = """
            # 第一章

            最初の章の本文です。十分な長さのテキストを入れます。

            ## 第一節

            節の本文です。こちらも十分な長さにします。テスト用の文章を続けます。

            # 第二章

            二章目の本文です。別セクションとして分割されることを期待します。
            """;

        var chunks = RagStructuralChunker.CreateChunks(text, source: "doc.md", size: 900, overlap: 128);

        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Chapter.Contains("第一章") || c.Text.Contains("最初の章"));
        Assert.Contains(chunks, c => c.Chapter.Contains("第二章") || c.Text.Contains("二章目"));
    }

    [Fact]
    public void CreateChunks_UnstructuredText_FallsBackToParagraphChunks()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("段落テキストです。", 30));
        var chunks = RagStructuralChunker.CreateChunks(text, source: "plain.txt", size: 120, overlap: 20);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }
}
