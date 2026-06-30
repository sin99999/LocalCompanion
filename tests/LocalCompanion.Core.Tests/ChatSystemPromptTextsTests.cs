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
        var line = ChatSystemPromptTexts.UserAndCharacterNameDistinction("太郎", "レン", japanese: true);
        Assert.Contains("太郎", line);
        Assert.Contains("レン", line);
    }

    [Fact]
    public void RagPriorityInstruction_RequiresCitingSourcesWhenRelevant()
    {
        var line = ChatSystemPromptTexts.RagPriorityInstruction(japanese: true);
        Assert.Contains("参考資料を優先", line);
        Assert.Contains("無関係", line);
    }
}
