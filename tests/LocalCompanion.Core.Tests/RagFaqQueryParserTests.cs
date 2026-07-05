using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagFaqQueryParserTests
{
    [Theory]
    [InlineData("有給休暇の取得方法は？", true)]
    [InlineData("VPNの接続手順を教えて", true)]
    [InlineData("贈賄罪とは", false)]
    [InlineData("hello", false)]
    public void TryGetQuestion_DetectsFaqStyleQueries(string query, bool expected)
    {
        Assert.Equal(expected, RagFaqQueryParser.TryGetQuestion(query, out _));
    }

    [Fact]
    public void TryGetQuestion_NormalizesKey()
    {
        Assert.True(RagFaqQueryParser.TryGetQuestion("有給休暇の取得方法は？", out var key));
        Assert.Contains("有給", key);
    }
}
