using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ReplySpeechCursorTests
{
    [Fact]
    public void Push_HoldsTextUntilASentenceEnds()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        var ready = cursor.Push("こんにちは", flush: false);
        Assert.Empty(ready);
    }

    [Fact]
    public void Push_EmitsTheFinishedSentenceAndHoldsTheRest()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        var ready = cursor.Push("こんにちは。今日は", flush: false);
        Assert.Equal(new[] { "こんにちは。" }, ready);
    }

    [Fact]
    public void Push_ContinuesFromTheHeldTail()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        cursor.Push("こんにちは。今日は", flush: false);
        var ready = cursor.Push("こんにちは。今日は晴れです。", flush: false);
        Assert.Equal(new[] { "今日は晴れです。" }, ready);
    }

    [Fact]
    public void Push_SameTextTwiceEmitsNothingTheSecondTime()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        cursor.Push("こんにちは。", flush: false);
        var ready = cursor.Push("こんにちは。", flush: false);
        Assert.Empty(ready);
    }

    [Fact]
    public void Push_FlushEmitsTheUnfinishedTail()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        var ready = cursor.Push("まだ途中", flush: true);
        Assert.Equal(new[] { "まだ途中" }, ready);
    }

    [Fact]
    public void Push_HardCutsALongRunWithoutPunctuation()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 10, maxTotalChars: 500);
        var text = new string('あ', 25);
        var ready = cursor.Push(text, flush: false);
        Assert.Equal(new[] { new string('あ', 10), new string('あ', 10) }, ready);
        Assert.Empty(cursor.Push(text, flush: false));
        var tail = cursor.Push(text, flush: true);
        Assert.Equal(new[] { new string('あ', 5) }, tail);
    }

    [Fact]
    public void Push_SkipsAClosedCodeFence()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 200, maxTotalChars: 500);
        var ready = cursor.Push("前文です。```python\nprint(1)\n```次です。", flush: false);
        Assert.Equal(new[] { "前文です。", "次です。" }, ready);
        Assert.DoesNotContain(ready, piece => piece.Contains("print", StringComparison.Ordinal));
    }

    [Fact]
    public void Push_HoldsAnUnclosedFenceAndReadsTheSentenceBeforeIt()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 200, maxTotalChars: 500);
        var first = cursor.Push("前文です。```python\nprint(1)", flush: false);
        Assert.Equal(new[] { "前文です。" }, first);
        var next = cursor.Push("前文です。```python\nprint(1)\n```次です。", flush: false);
        Assert.Equal(new[] { "次です。" }, next);
    }

    [Fact]
    public void Push_StripsEmojiAndBoldMarks()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        var ready = cursor.Push("**こんにちは。**✨次です。", flush: false);
        Assert.Equal(new[] { "こんにちは。", "次です。" }, ready);
    }

    [Fact]
    public void Push_StopsAtTheTotalCap()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 8);
        var ready = cursor.Push("あいうえおかきくけこ。", flush: true);
        Assert.Equal(new[] { "あいうえおかきく" }, ready);
        Assert.Empty(cursor.Push("あいうえおかきくけこ。さしすせそ。", flush: true));
    }

    [Fact]
    public void Push_GrowingFinalTextDoesNotRepeatTheFirstSentence()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        cursor.Push("こんにちは。", flush: false);
        var ready = cursor.Push("こんにちは。今日は晴れです。", flush: true);
        Assert.Equal(new[] { "今日は晴れです。" }, ready);
    }

    [Fact]
    public void Push_KeepsADecimalTogether()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        Assert.Empty(cursor.Push("値は1.", flush: false));
        var ready = cursor.Push("値は1.5です。", flush: false);
        Assert.Equal(new[] { "値は1.5です。" }, ready);
    }

    [Fact]
    public void Push_DoesNotRereadWhenTheOpeningIsRewritten()
    {
        var cursor = new ReplySpeechCursor(maxChunkChars: 80, maxTotalChars: 500);
        cursor.Push("こんにちは。今日は", flush: false);
        var ready = cursor.Push("別の文章です。", flush: true);
        Assert.Empty(ready);
    }
}
