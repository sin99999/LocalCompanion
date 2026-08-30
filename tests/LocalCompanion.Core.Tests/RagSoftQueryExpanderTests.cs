using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSoftQueryExpanderTests
{
    [Fact]
    public void Expand_Overtime_AddsLaborHoursTerms()
    {
        var expanded = RagSoftQueryExpander.Expand("残業の法律ってどうなってる？");
        Assert.Contains("残業", expanded, StringComparison.Ordinal);
        Assert.Contains("労働時間", expanded, StringComparison.Ordinal);
        Assert.Contains("四十時間", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_Shoplifting_AddsTheftTerms()
    {
        var expanded = RagSoftQueryExpander.Expand("万引きしたら捕まる？");
        Assert.Contains("窃盗", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_Kill_AddsMurderTerms()
    {
        var expanded = RagSoftQueryExpander.Expand("人を仕方なく殺す(ナイフ)場合は捕まる？");
        Assert.Contains("殺人", expanded, StringComparison.Ordinal);
        Assert.Contains("殺害", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_Abduction_AddsKidnapTerms()
    {
        var expanded = RagSoftQueryExpander.Expand("未成年略取とか無かった？");
        Assert.Contains("略取", expanded, StringComparison.Ordinal);
        Assert.Contains("誘拐", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_NonConsensualSex_AddsRelatedTerms()
    {
        var expanded = RagSoftQueryExpander.Expand("不同意性交とかは？");
        Assert.Contains("不同意性交", expanded, StringComparison.Ordinal);
        Assert.Contains("強制性交", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_Unrelated_KeepsOriginal()
    {
        Assert.Equal("今日は暑いね", RagSoftQueryExpander.Expand("今日は暑いね"));
    }
}
