using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxClientTextTests
{
    [Fact]
    public void CleanSpeakText_StripsMarkdownAndEmoji()
    {
        var cleaned = VoicevoxClient.CleanSpeakText("**こんにちは** `code` ✨\n次の行");
        Assert.Equal("こんにちは 次の行", cleaned);
    }

    [Fact]
    public void PrepareSpeakText_PreferSentenceEnd_CutsAtPeriod()
    {
        var text = new string('あ', 30) + "。" + new string('い', 20);
        var prepared = VoicevoxClient.PrepareSpeakText(text, maxChars: 40, preferSentenceEnd: true);
        Assert.Equal(new string('あ', 30) + "。", prepared);
    }

    [Fact]
    public void PrepareSpeakText_OverLimitWithoutSentence_AddsEllipsis()
    {
        var text = new string('あ', 50);
        var prepared = VoicevoxClient.PrepareSpeakText(text, maxChars: 20, preferSentenceEnd: false);
        Assert.EndsWith("…", prepared);
        Assert.Equal(21, prepared.Length);
    }
}
