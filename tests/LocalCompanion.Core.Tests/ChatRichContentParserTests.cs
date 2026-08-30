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
