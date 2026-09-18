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
        Assert.Equal(["名前", "数"], table.TableHeader);
        Assert.Equal(["a", "1"], Assert.Single(table.TableRows));
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
    public void ParseBlocks_FencedMarkdownTable_IsTableNotCode()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            ```
            | 名前 | 数 |
            | --- | --- |
            | a | 1 |
            ```
            """,
            sentenceBreaks: false);

        var table = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Table, table.Kind);
        Assert.Equal(["名前", "数"], table.TableHeader);
        Assert.Equal(["a", "1"], Assert.Single(table.TableRows));
    }

    [Fact]
    public void ParseBlocks_FencedJammedTable_IsTableNotCode()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            ```
            | 行 | 列 | 中身（例文） | ---------------- | 主体 | 1 | 服装／対象名 | 2 | 髪型・色 |
            ```
            """,
            sentenceBreaks: false);

        Assert.DoesNotContain(blocks, b => b.Kind == ChatDisplayBlockKind.Code);
        var table = Assert.Single(blocks, b => b.Kind == ChatDisplayBlockKind.Table);
        Assert.Contains("行", table.TableHeader);
        Assert.Contains("列", table.TableHeader);
        Assert.Contains("主体", table.TableHeader.Concat(table.TableRows.SelectMany(r => r)));
    }

    [Fact]
    public void ParseBlocks_FencedList_IsListNotCode()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            ```
            - りんご
            - みかん
            ```
            """,
            sentenceBreaks: false);

        var list = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.List, list.Kind);
        Assert.Equal(["りんご", "みかん"], list.ListItems);
    }

    [Fact]
    public void ParseBlocks_FencedPowerShell_StaysCode()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            """
            ```powershell
            dotnet build LocalCompanion.csproj -c Debug
            ```
            """,
            sentenceBreaks: false);

        var code = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Code, code.Kind);
        Assert.Contains("dotnet build", code.CodeText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseBlocks_UnfencedCommand_IsCode()
    {
        var blocks = ChatRichContentParser.ParseBlocks(
            "dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64",
            sentenceBreaks: false);

        var code = Assert.Single(blocks);
        Assert.Equal(ChatDisplayBlockKind.Code, code.Kind);
        Assert.StartsWith("dotnet build", code.CodeText);
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

    [Fact]
    public void ParseBlocks_JammedOneLineMarkdownTable_BecomesFourColumnTable()
    {
        const string body =
            "The layout forms a compact3x4 table where the viewer sees:| apple | banana | cherry | date ||-------|--------|--------|--------|| elephant | fox | grape | house || igloo | jellyfish | kiwi | lemon |";

        var blocks = ChatRichContentParser.ParseBlocks(body, sentenceBreaks: true);
        var table = Assert.Single(blocks, b => b.Kind == ChatDisplayBlockKind.Table);
        Assert.Equal(["apple", "banana", "cherry", "date"], table.TableHeader);
        Assert.Equal(2, table.TableRows.Count);
        Assert.Equal(["elephant", "fox", "grape", "house"], table.TableRows[0]);
        Assert.Equal(["igloo", "jellyfish", "kiwi", "lemon"], table.TableRows[1]);
    }

    [Fact]
    public void ParseBlocks_TabSeparatedThreeByFourGrid_BecomesTable()
    {
        const string body = "apple\tbanana\tcherry\tdate\nelephant\tfox\tgrape\thouse\nigloo\tjellyfish\tkiwi\tlemon";

        var blocks = ChatRichContentParser.ParseBlocks(body, sentenceBreaks: false);
        var table = Assert.Single(blocks, b => b.Kind == ChatDisplayBlockKind.Table);
        Assert.Equal(["apple", "banana", "cherry", "date"], table.TableHeader);
        Assert.Equal(2, table.TableRows.Count);
        Assert.Equal(["elephant", "fox", "grape", "house"], table.TableRows[0]);
        Assert.Equal(["igloo", "jellyfish", "kiwi", "lemon"], table.TableRows[1]);
    }
}
