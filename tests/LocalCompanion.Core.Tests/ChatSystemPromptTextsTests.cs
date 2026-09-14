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

    /// <summary>共通指示 1 本: 資料優先・無関係なら無視・数値はそのまま・口調維持。</summary>
    [Fact]
    public void RagGroundingInstruction_CoversPriorityIgnoreNumbersAndVoice()
    {
        var ja = ChatSystemPromptTexts.RagGroundingInstruction(japanese: true);
        Assert.Contains("資料を優先", ja);
        Assert.Contains("無関係", ja);
        Assert.Contains("数値", ja);
        Assert.Contains("そのまま使い", ja);
        Assert.Contains("口調", ja);
        Assert.Contains(ChatSystemPromptTexts.RagHitsHeader(japanese: true), ja);

        var en = ChatSystemPromptTexts.RagGroundingInstruction(japanese: false);
        Assert.Contains(ChatSystemPromptTexts.RagHitsHeader(japanese: false), en);
        Assert.Contains("exactly as written", en);
    }

    /// <summary>法令の追加指示は、罰則文言・単一条の有無で行が増える。</summary>
    [Fact]
    public void RagLegalAddendum_AddsPenaltyAndSingleArticleLinesOnlyWhenAsked()
    {
        var bare = ChatSystemPromptTexts.RagLegalAddendum(japanese: true, quotePenalty: false, singleArticle: false);
        Assert.Contains("質問された条", bare);
        Assert.Contains("関連", bare);
        Assert.DoesNotContain("引用必須", bare);
        Assert.DoesNotContain("範囲に限る", bare);

        var full = ChatSystemPromptTexts.RagLegalAddendum(japanese: true, quotePenalty: true, singleArticle: true);
        Assert.Contains("【資料記載の罰則文言（引用必須）】", full);
        Assert.Contains("範囲に限る", full);

        var en = ChatSystemPromptTexts.RagLegalAddendum(japanese: false, quotePenalty: true, singleArticle: false);
        Assert.Contains("【資料記載の罰則文言（引用必須）】", en);
    }

    /// <summary>返答言語は 1 本。日本語／他言語、キャラクター有無で口調行だけ変わる。</summary>
    [Fact]
    public void ReplyLanguageInstruction_SingleBlock_PersonaLineOnlyForCharacter()
    {
        var jaPlain = ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: true, isCharacter: false);
        Assert.Contains("日本語だけ", jaPlain);
        Assert.DoesNotContain("口調", jaPlain);

        var jaChar = ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: true, isCharacter: true);
        Assert.Contains("口調", jaChar);

        var enChar = ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: false, isCharacter: true);
        Assert.Contains("not in Japanese", enChar);
        Assert.Contains("Persona text above", enChar);
        Assert.DoesNotContain("Persona text above", ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: false, isCharacter: false));
    }

    /// <summary>毎ターン必ず載る固定文言（役割・読みやすさ・長さ・返答言語）は日本語で 700 字以下に抑える。</summary>
    [Fact]
    public void AlwaysOnJapaneseInstructions_StayUnder700Chars()
    {
        var total =
            ChatSystemPromptTexts.DefaultAssistantLine(japanese: true).Length
            + ChatSystemPromptTexts.ReadabilityInstruction(japanese: true).Length
            + ChatReplyLimitHelper.SystemLimitNote(10_000, japanese: true).Length
            + ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: true, isCharacter: true).Length;

        Assert.True(total <= 700, $"always-on instructions: {total} chars");
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
    public void HistoryDigestInstruction_ForbidsQuotingAsVerbatim()
    {
        var ja = ChatSystemPromptTexts.HistoryDigestInstruction(japanese: true);
        Assert.Contains("原文のように引用しない", ja);
        Assert.Contains("直近の会話履歴を優先", ja);

        var en = ChatSystemPromptTexts.HistoryDigestInstruction(japanese: false);
        Assert.Contains("Do not quote it as verbatim", en);
        Assert.Contains("Prefer the recent chat history", en);
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
            ChatSystemPromptTexts.DefaultAssistantLine(japanese: true),
            ChatSystemPromptTexts.ReplyLanguageInstruction(japanese: true, isCharacter: true),
            ChatSystemPromptTexts.ReadabilityInstruction(japanese: true),
            ChatSystemPromptTexts.UserNameLine("太郎", japanese: true),
            ChatSystemPromptTexts.CharacterNameLine("花子", japanese: true),
            ChatSystemPromptTexts.UserAndCharacterNameDistinction("太郎", "花子", japanese: true),
            ChatSystemPromptTexts.SpontaneousMemoryInstruction(japanese: true),
            ChatSystemPromptTexts.HistoryDigestInstruction(japanese: true),
            ChatSystemPromptTexts.RagGroundingInstruction(japanese: true),
            ChatSystemPromptTexts.RagAdvisoryInstruction(japanese: true),
            ChatSystemPromptTexts.RagLegalAddendum(japanese: true, quotePenalty: true, singleArticle: true),
            ChatSystemPromptTexts.RagRiskCautionInstruction(japanese: true),
        };

        foreach (var text in texts)
        {
            var stripped = text.Replace("キャラクター", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("キャラ", stripped);
        }
    }

    /// <summary>出どころの区別は参考資料の見出し語と同じ語を使う（見出し名の食い違いで無視されない）。</summary>
    [Fact]
    public void MemoryDistinction_UsesSameHeaderWordAsRagBlock()
    {
        Assert.Contains(ChatSystemPromptTexts.RagHitsHeader(japanese: true), ChatSystemPromptTexts.MemoryDistinction(japanese: true));
        Assert.Contains(ChatSystemPromptTexts.RagHitsHeader(japanese: false), ChatSystemPromptTexts.MemoryDistinction(japanese: false));
    }
}
