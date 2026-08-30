using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagArticleQueryParserTests
{
    [Theory]
    [InlineData("労基法の8条全文を教えて", 8)]
    [InlineData("第8条", 8)]
    [InlineData("第８条（適用事業の範囲）", 8)]
    [InlineData("労働基準法 15条", 15)]
    [InlineData("刑法第199条", 199)]
    [InlineData("労働基準法第999条の全文を出して", 999)]
    public void TryGetArticleNumber_ParsesJapaneseLegalQueries(string query, int expected)
    {
        Assert.True(RagArticleQueryParser.TryGetArticleNumber(query, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("賃金の支払いはいつ")]
    [InlineData("労働時間について")]
    [InlineData("")]
    public void TryGetArticleNumber_ReturnsFalseForGeneralQueries(string query)
    {
        Assert.False(RagArticleQueryParser.TryGetArticleNumber(query, out _));
    }

    [Fact]
    public void GetArticleNumbers_CollectsComparisonQuery()
    {
        var numbers = RagArticleQueryParser.GetArticleNumbers("刑法4条と104条の違い");
        Assert.Equal([4, 104], numbers);
    }

    [Fact]
    public void BuildHeaderPrefixes_IncludesHalfAndFullWidth()
    {
        var prefixes = RagArticleQueryParser.BuildHeaderPrefixes(8);
        Assert.Contains("第8条", prefixes);
        Assert.Contains("第８条", prefixes);
    }

    [Theory]
    [InlineData("最後の条文は第何条？", 1)]
    [InlineData("労基法の全体の最後の条文", 1)]
    [InlineData("最初の条文を教えて", 0)]
    public void TryGetBoundaryIntent_DetectsWholeLawBoundary(string query, int expectedIntent)
    {
        Assert.True(RagArticleQueryParser.TryGetBoundaryIntent(query, out var intent));
        Assert.Equal(expectedIntent, (int)intent);
    }

    [Theory]
    [InlineData("第8条の最後の段落は？")]
    [InlineData("賃金の支払いはいつ")]
    public void TryGetBoundaryIntent_ReturnsFalseForNonBoundaryQueries(string query)
    {
        Assert.False(RagArticleQueryParser.TryGetBoundaryIntent(query, out _));
    }

    [Theory]
    [InlineData("第134条", 13400L)]
    [InlineData("第96条の2（監督上の行政措置）", 9602L)]
    [InlineData("第90条（作成の手続）", 9000L)]
    [InlineData("#### 第8条（他の法令の罪に対する適用）", 800L)]
    public void TryParseArticleSortKey_ParsesHeaderText(string header, long expected)
    {
        Assert.True(RagArticleQueryParser.TryParseArticleSortKey(header, out var key));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void ExtractSourceHint_FindsLaborLawNickname()
    {
        Assert.Equal("労働基準法", RagArticleQueryParser.ExtractSourceHint("労基法の全体の最後の条文"));
    }
}
