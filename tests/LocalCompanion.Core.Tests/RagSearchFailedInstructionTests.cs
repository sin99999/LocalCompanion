using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSearchFailedInstructionTests
{
    [Fact]
    public void RagEmptyHitsInstruction_WhenSearchFailed_UsesFailedCopy()
    {
        var text = ChatSystemPromptTexts.RagEmptyHitsInstruction(japanese: true, searchFailed: true);
        Assert.Contains("検索未完了", text);
        Assert.DoesNotContain("資料なし", text);
    }

    [Fact]
    public void RagEmptyHitsInstruction_WhenMiss_UsesMissCopy()
    {
        var text = ChatSystemPromptTexts.RagEmptyHitsInstruction(japanese: true, searchFailed: false);
        Assert.Contains("資料なし", text);
        Assert.DoesNotContain("検索未完了", text);
    }
}
