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

    /// <summary>本文が「第235条から…」で始まる参照文を、条見出しと誤認しない（第243条の本文が消える退行を防ぐ）。</summary>
    [Fact]
    public void CreateChunks_BodyLineStartingWithArticleReference_StaysInBody()
    {
        var text = """
            #### 第243条（未遂罪）

            第235条から第236条までの罪の未遂は、罰する。

            #### 第244条（親族間の犯罪に関する特例）

            配偶者との間で第235条の罪を犯した者は、その刑を免除する。
            """;

        var chunks = RagStructuralChunker.CreateChunks(text, source: "法令抜粋.md", size: 900, overlap: 128);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(243, chunks[0].ArticleMain);
        Assert.Contains("第235条から第236条までの罪の未遂", chunks[0].Text, StringComparison.Ordinal);
        Assert.Equal(244, chunks[1].ArticleMain);
    }

    /// <summary>平文の「第1条（目的）」行＋本文行は、引き続き条見出しとして扱う。</summary>
    [Fact]
    public void CreateChunks_PlainArticleHeaderLine_IsStillHeader()
    {
        var text = "第1条（目的）\nこの法律は、テスト用の条文です。十分な長さの本文にします。\n\n第2条\n二番目の条文です。こちらも十分な長さにします。\n";

        var chunks = RagStructuralChunker.CreateChunks(text, source: "plain-law.txt", size: 900, overlap: 128);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(1, chunks[0].ArticleMain);
        Assert.Equal(2, chunks[1].ArticleMain);
    }

    [Fact]
    public void CreateChunks_UnstructuredText_FallsBackToParagraphChunks()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("段落テキストです。", 30));
        var chunks = RagStructuralChunker.CreateChunks(text, source: "plain.txt", size: 120, overlap: 20);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void CreateChunks_OversizedSection_SetsParentText()
    {
        var body = string.Join("\n", Enumerable.Repeat("条文本文の一行です。", 80));
        var text = $"""
            ### 第8条

            {body}
            """;

        var chunks = RagStructuralChunker.CreateChunks(text, source: "law.md", size: 200, overlap: 20);

        Assert.True(chunks.Count > 1);
        var withParent = chunks.Where(c => !string.IsNullOrWhiteSpace(c.ParentText)).ToList();
        Assert.NotEmpty(withParent);
        Assert.All(withParent, c => Assert.Contains("第8条", c.ParentText));
        Assert.All(withParent, c => Assert.True(c.ArticleSortKey > 0));
    }
}
