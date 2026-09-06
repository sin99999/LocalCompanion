using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatChoiceReplyExpanderTests
{
    [Fact]
    public void Expand_BareNumber_UsesLastNumberedListItem()
    {
        const string assistant = """
            【次の展開はどうする？】
            1. [夢中で抱きつく]
            2. [黙って見守る]
            3. [名前を呼ぶ]
            """;

        var expanded = ChatChoiceReplyExpander.Expand("3", assistant);

        Assert.Equal("3. [名前を呼ぶ]", expanded);
    }

    [Fact]
    public void Expand_NumberWithPeriod_UsesMatchingItem()
    {
        const string assistant = """
            1. 走る
            2. 隠れる
            """;

        Assert.Equal("2. 隠れる", ChatChoiceReplyExpander.Expand("2.", assistant));
    }

    [Fact]
    public void Expand_FullwidthDigit_UsesMatchingItem()
    {
        const string assistant = """
            1. りんご
            2. みかん
            3. ぶどう
            """;

        Assert.Equal("3. ぶどう", ChatChoiceReplyExpander.Expand("３", assistant));
    }

    [Fact]
    public void Expand_DashList_UsesNthItem()
    {
        const string assistant = """
            提案:
            - 赤
            - 青
            - 緑
            """;

        Assert.Equal("3. 緑", ChatChoiceReplyExpander.Expand("3", assistant));
    }

    [Fact]
    public void Expand_LetteredOption_UsesMatchingLetter()
    {
        const string assistant = """
            次はどうする？
            A. 夢中で
            B. 逃げる
            C. 泣く
            """;

        Assert.Equal("B. 逃げる", ChatChoiceReplyExpander.Expand("b", assistant));
    }

    [Fact]
    public void Expand_NoList_LeavesBareNumber()
    {
        Assert.Equal("3", ChatChoiceReplyExpander.Expand("3", "今日はいい天気だ。"));
    }

    [Fact]
    public void Expand_IndexPastList_LeavesBareNumber()
    {
        const string assistant = """
            1. ひとつ
            2. ふたつ
            """;

        Assert.Equal("3", ChatChoiceReplyExpander.Expand("3", assistant));
    }

    [Fact]
    public void Expand_ArticleLikeText_IsNotABareChoice()
    {
        const string assistant = """
            1. りんご
            2. みかん
            3. ぶどう
            """;

        Assert.Equal("3条", ChatChoiceReplyExpander.Expand("3条", assistant));
    }

    [Fact]
    public void Expand_AlreadyFullText_DoesNotDouble()
    {
        const string assistant = """
            1. りんご
            2. みかん
            """;

        Assert.Equal("2. みかん", ChatChoiceReplyExpander.Expand("2. みかん", assistant));
    }

    [Fact]
    public void Expand_EnglishI_WithoutLetteredOption_LeavesText()
    {
        Assert.Equal("I", ChatChoiceReplyExpander.Expand("I", "Hello there."));
    }

    [Fact]
    public void Expand_CornerTitleOnSameLine_StillFindsChoices()
    {
        const string assistant =
            "彼によって暴かれていく感覚が、たまらなく幸せだった……！)*******【次の展開はどうする？】1. [夢中で] 2. [黙って] 3. [名前を呼ぶ]";

        var expanded = ChatChoiceReplyExpander.Expand("3", assistant);
        Assert.Equal("3. [名前を呼ぶ]", expanded);
    }
}
