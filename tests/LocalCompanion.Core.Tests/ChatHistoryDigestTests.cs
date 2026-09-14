using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatHistoryDigestTests
{
    [Fact]
    public void IsAllowedPreset_NamedCharacterOnly()
    {
        Assert.False(ChatHistoryDigest.IsAllowedPreset(CharacterPresetService.DefaultAiPresetKey));
        Assert.True(ChatHistoryDigest.IsAllowedPreset("example.json"));
    }

    [Fact]
    public void Pick_BudgetExceeded_OlderRowsAreDropped_RecentStayVerbatim()
    {
        var rows = new List<ChatHistoryRow>
        {
            new(3, "user", "新しい質問です。"),
            new(2, "assistant", "前の答えです。"),
            new(1, "user", "とても長い古い話。" + new string('あ', 80)),
        };

        var pick = ChatHistoryDigest.Pick(rows, historyCharBudget: 40);

        Assert.Contains(pick.Kept, t => t.Content.Contains("新しい質問", StringComparison.Ordinal));
        Assert.DoesNotContain(pick.Kept, t => t.Content.Contains("とても長い古い話", StringComparison.Ordinal));
        Assert.Contains(pick.Dropped, r => r.Id == 1);
        Assert.DoesNotContain(pick.Dropped, r => r.Id == 3);
    }

    [Fact]
    public void NeedsRefresh_SameUpto_IsFalse()
    {
        ChatHistoryRow[] dropped = [new(5, "user", "古い")];
        Assert.False(ChatHistoryDigest.NeedsRefresh(5, dropped));
        Assert.True(ChatHistoryDigest.NeedsRefresh(4, dropped));
    }

    [Fact]
    public void TryAppendPrompt_Empty_DoesNotAddInstruction()
    {
        var parts = new List<string> { "core" };
        Assert.False(ChatHistoryDigest.TryAppendPrompt(parts, "", japanese: true));
        Assert.DoesNotContain("【古い会話の要約】", string.Join('\n', parts));
        Assert.DoesNotContain("【古い会話の要約の扱い】", string.Join('\n', parts));
    }

    [Fact]
    public void TryAppendPrompt_NonEmpty_AddsBlockAndInstruction()
    {
        var parts = new List<string>();
        Assert.True(ChatHistoryDigest.TryAppendPrompt(parts, "麦茶が好き", japanese: true));
        var text = string.Join('\n', parts);
        Assert.Contains("【古い会話の要約】", text);
        Assert.Contains("麦茶が好き", text);
        Assert.Contains("【古い会話の要約の扱い】", text);
        Assert.Contains("原文のように引用しない", text);
    }
}
