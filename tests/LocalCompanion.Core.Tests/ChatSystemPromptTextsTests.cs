using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatSystemPromptTextsTests
{
    [Fact]
    public void SpeakingStyleLine_IncludesStyleInJapanese()
    {
        var line = ChatSystemPromptTexts.SpeakingStyleLine("丁寧語で話す", japanese: true);
        Assert.Contains("丁寧語で話す", line);
        Assert.Contains("話し方", line);
    }

    [Fact]
    public void UserAndCharacterNameDistinction_ListsBothNames()
    {
        var line = ChatSystemPromptTexts.UserAndCharacterNameDistinction("太郎", "花子", japanese: true);
        Assert.Contains("太郎", line);
        Assert.Contains("花子", line);
    }

    [Fact]
    public void RagPriorityInstruction_RequiresCitingSourcesWhenRelevant()
    {
        var line = ChatSystemPromptTexts.RagPriorityInstruction(japanese: true);
        Assert.Contains("参考資料を優先", line);
        Assert.Contains("無関係", line);
        Assert.Contains("数値", line);
        Assert.Contains("置き換えない", line);
        Assert.Contains("引用必須", line);
    }

    [Fact]
    public void SpontaneousMemoryInstruction_DisallowsMemoryListMetaTalk()
    {
        var ja = ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: true);
        Assert.Contains("ひとつだけ", ja);
        Assert.Contains("メタ説明は禁止", ja);

        var en = ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: false);
        Assert.Contains("occasionally", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not mention databases", en);
    }

    [Fact]
    public void FormatForSystemPrompt_IncludesPrivateGuidance()
    {
        var block = MemoryService.FormatForSystemPrompt(
            [new UserMemoryRecord(1, "好きな飲み物は麦茶", "session", "s1", "2026-01-01")],
            japanese: true);
        Assert.Contains("心の中の長期記憶", block);
        Assert.Contains("好きな飲み物は麦茶", block);
        Assert.Contains("メタな言い方はしない", block);
    }
}
