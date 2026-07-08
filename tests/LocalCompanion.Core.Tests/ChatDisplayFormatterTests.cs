using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatDisplayFormatterTests
{
    [Fact]
    public void FormatForDisplay_JapaneseSentenceEnd_InsertsNewlineBeforeNextSentence()
    {
        var actual = ChatDisplayFormatter.FormatForDisplay("お疲れ！今日はどう？");
        Assert.Equal("お疲れ！\n今日はどう？", actual);
    }

    [Fact]
    public void FormatForDisplay_PunctuationThenEmoji_KeepsSameLine()
    {
        // 表示用句点改行が 「！😘💕」 を割らないこと（UI で絵文字だけ次行になる退行防止）
        var actual = ChatDisplayFormatter.FormatForDisplay("全部レンにぶつけていいからさー！😘💕");
        Assert.Equal("全部レンにぶつけていいからさー！😘💕", actual);
        Assert.DoesNotContain('\n', actual);
    }

    [Fact]
    public void FormatForDisplay_PunctuationThenEmojiThenMoreText_KeepsSameLine()
    {
        var actual = ChatDisplayFormatter.FormatForDisplay("こんばんわ〜〜！🌙✨また来てくれたんだ〜！");
        Assert.Equal("こんばんわ〜〜！🌙✨また来てくれたんだ〜！", actual);
    }

    [Fact]
    public void FormatForDisplay_SentenceBreaksDisabled_LeavesText()
    {
        var raw = "お疲れ！今日はどう？";
        Assert.Equal(raw, ChatDisplayFormatter.FormatForDisplay(raw, sentenceBreaks: false));
    }
}
