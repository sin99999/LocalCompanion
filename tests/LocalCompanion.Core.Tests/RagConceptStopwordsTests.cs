using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>実 DB の上位語に混ざった接続語・活用形（及び 125 チャンク、処する 106、若しくは 91…）を概念にしない。</summary>
public sealed class RagConceptStopwordsTests
{
    [Theory]
    [InlineData("及び")]
    [InlineData("若しくは")]
    [InlineData("又は")]
    [InlineData("処する")]
    [InlineData("罰する")]
    [InlineData("関する")]
    [InlineData("定める")]
    [InlineData("適用する")]
    [InlineData("受け")]
    [InlineData("対し")]
    [InlineData("犯し")]
    [InlineData("限り")]
    [InlineData("係る")]
    [InlineData("掲げ")]
    [InlineData("第1項")]
    [InlineData("第一項")]
    [InlineData("前項")]
    [InlineData("同様")]
    [InlineData("")]
    public void IsStopword_ConjunctionsAndInflections_True(string term) =>
        Assert.True(RagConceptStopwords.IsStopword(term));

    [Theory]
    [InlineData("窃盗")]
    [InlineData("労働時間")]
    [InlineData("罰金")]
    [InlineData("禁錮")]
    [InlineData("手続き")]
    [InlineData("取り扱い")]
    [InlineData("適用")]
    [InlineData("賃金")]
    [InlineData("第235条")]
    [InlineData("webview2")]
    [InlineData("dotnet")]
    public void IsStopword_ContentWords_False(string term) =>
        Assert.False(RagConceptStopwords.IsStopword(term));
}
