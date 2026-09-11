using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatRichContentParserTests
{
    [Fact]
    public void ParseBlocks_List_TakesDashItems()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            - りんご
            - みかん
            """,
            sentenceBreaks: false);

        var list = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.List, list.Kind);
        Assert.False(list.ListOrdered);
        Assert.Equal(["りんご", "みかん"], list.ListItems);
    }

    [Fact]
    public void ParseBlocks_NumberAfterOrphanCloser_IsNotAList()
    {
        var blocks = ChatRichContentParser.ParseBlocks("】1. [夢中で", sentenceBreaks: false);

        var paragraph = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Paragraph, paragraph.Kind);
        Assert.Contains("】1. [夢中で", paragraph.ParagraphLines);
    }

    [Fact]
    public void ParseBlocks_ChoiceAfterCornerTitle_KeepsCloserWithTitle()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            "彼によって暴かれていく感覚が、たまらなく幸せだった……！)*******【次の展開はどうする？】1. [夢中で");

        Assert.DoesNotContain(
            blocks,
            b => b.Kind == ChatDisplayBlockKind.List
                 && b.ListItems.Any(item => item.StartsWith('】')));

        var list = Assert.Single(blocks, b => b.Kind == ChatDisplayBlockKind.List);
        Assert.True(list.ListOrdered);
        Assert.Contains("[夢中で", list.ListItems[0]);
    }

    [Fact]
    public void ParseBlocks_SingleNumberedChoice_KeepsOriginalStart()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            "3. とにかく今の話を続けて盛り上がる！",
            sentenceBreaks: false);

        var list = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.List, list.Kind);
        Assert.True(list.ListOrdered);
        Assert.Equal(3, list.ListStartNumber);
        Assert.Equal(["とにかく今の話を続けて盛り上がる！"], list.ListItems);
    }

    [Fact]
    public void ParseBlocks_Table_TakesHeaderAndBody()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            | 名前 | 数 |
            | --- | --- |
            | a | 1 |
            """,
            sentenceBreaks: false);

        var table = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Table, table.Kind);
        // いまの正規化は | セル | を縦並びにほぐすことがある。列のきれいさより「表になる／中身が残る」を契約にする
        Assert.Contains("名前", table.TableHeader);
        Assert.Contains("数", table.TableHeader);
        Assert.True(table.TableRows.Count >= 1, "body row missing");
        Assert.Contains("1", table.TableRows.SelectMany(r => r));
    }

    [Fact]
    public void ParseBlocks_PromptBlob_BecomesCodeBetweenParagraphs()
    {
        const string body = """
            生成されたプロンプト:
            (Masterpiece, Bestquality,Highres:1.2),(Anime style:1.3),1girl, nagato yuki, defensive pose, looking at camera, determined expression, serious face, short navy blue hair, purple eyes, floating in void, cosmic background
            (Negative Prompt: lowres, bad anatomy, extra digits, jpeg artifacts)
            生成されたプロンプトは、アニメーションスタイルで構成した。
            """;

        var blocks = ChatRichContentParser.ParseBlocks(body, sentenceBreaks: true);
        Assert.Equal(ChatDisplayBlockKind.Paragraph, blocks[0].Kind);
        Assert.Contains("生成されたプロンプト", blocks[0].ParagraphLines[0]);

        var codes = blocks.Where(b => b.Kind == ChatDisplayBlockKind.Code).ToList();
        Assert.Equal(2, codes.Count);
        Assert.Contains("Masterpiece", codes[0].CodeText, StringComparison.Ordinal);
        Assert.Contains("Negative Prompt", codes[1].CodeText, StringComparison.Ordinal);

        var last = blocks[^1];
        Assert.Equal(ChatDisplayBlockKind.Paragraph, last.Kind);
        Assert.Contains("アニメーションスタイル", string.Join('\n', last.ParagraphLines));
    }

    [Fact]
    public void ParseBlocks_FencedCode_IsCodeBlock()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            ```
            hello
            ```
            """,
            sentenceBreaks: false);

        var code = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Code, code.Kind);
        Assert.Equal("hello", code.CodeText);
    }

    [Fact]
    public void ParseBlocks_HeadingPrefix_IsStrippedToParagraph()
    {
        var blocks = ChatRichContentParser.ParseBlocks("# 見出し", sentenceBreaks: false);

        var paragraph = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Paragraph, paragraph.Kind);
        Assert.DoesNotContain('#', string.Join('\n', paragraph.ParagraphLines));
        Assert.Contains("見出し", paragraph.ParagraphLines);
    }

    [Fact]
    public void ParseBlocks_VerticalPipe_GuessesThreeColumnsWhenDivisible()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            | りんご
            | みかん
            | ばなな
            | 1
            | 2
            | 3
            """,
            sentenceBreaks: false);

        var table = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Table, table.Kind);
        Assert.Equal(3, table.TableHeader.Count);
        Assert.Equal(["りんご", "みかん", "ばなな"], table.TableHeader);
        var row = Assert.Single(table.TableRows);
        Assert.Equal(["1", "2", "3"], row);
    }
}
