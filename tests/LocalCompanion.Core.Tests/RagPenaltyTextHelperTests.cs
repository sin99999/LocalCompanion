using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagPenaltyTextHelperTests
{
    [Fact]
    public void ExtractLeadingPenaltySentence_FindsPenaltyLine()
    {
        const string text = """
            第百九十七条から第百九十七条の四までに 規定する賄賂を供与し、又はその申込み若しくは約束をした者は、三年以下の懲役又は二百五十万円以下の罰金に処する。
            """;

        var line = RagPenaltyTextHelper.ExtractLeadingPenaltySentence(text);
        Assert.NotNull(line);
        Assert.Contains("三年以下", line);
        Assert.Contains("二百五十万円", line);
    }

    [Fact]
    public void FormatForPrompt_HighlightsPenaltySentence()
    {
        var hit = new RagSearchHit(
            """
            第百九十七条から第百九十七条の四までに 規定する賄賂を供与し、又はその申込み若しくは約束をした者は、三年以下の懲役又は二百五十万円以下の罰金に処する。
            """,
            "刑法.md",
            "第8条（贈賄）",
            0,
            "chunk",
            "",
            "三年以下の懲役又は二百五十万円以下の罰金に処する。",
            "三年以下の懲役又は二百五十万円以下の罰金に処する。");

        var prompt = hit.FormatForPrompt(0);
        Assert.Contains("【資料記載の罰則文言（引用必須）】", prompt);
        Assert.Contains("三年以下", prompt);
    }
}
