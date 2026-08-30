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
    public void RagMissInstruction_ForbidsInventingArticles()
    {
        var ja = ChatSystemPromptTexts.RagMissInstruction(japanese: true);
        Assert.Contains("見つかりませんでした", ja);
        Assert.Contains("推測で作らない", ja);
    }

    [Fact]
    public void SpontaneousMemoryInstruction_DisallowsMemoryListMetaTalk()
    {
        var ja = ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: true);
        Assert.Contains("1件まで", ja);
        Assert.Contains("そういえば", ja);
        Assert.Contains("メタ説明は禁止", ja);
        Assert.DoesNotContain("たまに", ja);

        var en = ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: false);
        Assert.Contains("at most one", en, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("occasionally", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not mention databases", en);
    }

    [Fact]
    public void AttachmentInstruction_AsksToCiteReferenceUrls()
    {
        var ja = ChatSystemPromptTexts.AttachmentInstruction(japanese: true);
        Assert.Contains("参考URL", ja);
        Assert.Contains("参考:", ja);
        Assert.Contains("登録資料しか調べられない", ja);

        var en = ChatSystemPromptTexts.AttachmentInstruction(japanese: false);
        Assert.Contains("Reference URLs", en);
        Assert.Contains("Sources:", en);
        Assert.Contains("registered documents", en);
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
        Assert.Contains("そういえば", block);
        Assert.DoesNotContain("穏やかで隙", block);
    }

    [Fact]
    public void JapaneseInstructions_SpellCharacterInFull()
    {
        var texts = new[]
        {
            ChatSystemPromptTexts.DefaultLanguageInstruction(japanese: true),
            ChatSystemPromptTexts.CharacterLanguageInstruction(japanese: true),
            ChatSystemPromptTexts.ReadabilityInstruction(japanese: true),
            ChatSystemPromptTexts.UserNameLine("太郎", japanese: true),
            ChatSystemPromptTexts.CharacterNameLine("花子", japanese: true),
            ChatSystemPromptTexts.UserAndCharacterNameDistinction("太郎", "花子", japanese: true),
            ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: true),
            ChatSystemPromptTexts.RagPersonaReferenceInstruction(japanese: true),
            ChatSystemPromptTexts.RagAdvisoryInstruction(japanese: true),
            ChatSystemPromptTexts.RagArticleScopeInstruction(japanese: true),
            ChatSystemPromptTexts.RagCitationFirstInstruction(japanese: true),
        };

        foreach (var text in texts)
        {
            var stripped = text.Replace("キャラクター", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("キャラ", stripped);
        }
    }

    [Fact]
    public void RagPersonaReferenceInstruction_ForbidsMixingUnaskedArticles()
    {
        var ja = ChatSystemPromptTexts.RagPersonaReferenceInstruction(japanese: true);
        Assert.DoesNotContain("関連する条項チェック", ja);
        Assert.Contains("指定されていない条番号", ja);

        var scope = ChatSystemPromptTexts.RagArticleScopeInstruction(japanese: true);
        Assert.Contains("質問された条", scope);
        Assert.Contains("関連", scope);
    }
}
