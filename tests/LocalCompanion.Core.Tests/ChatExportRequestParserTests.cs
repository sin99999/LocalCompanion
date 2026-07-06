using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportRequestParserTests
{
    [Theory]
    [InlineData("刑法第54条を調べて結果をデスクトップにmdで置いといて", true, "刑法第54条を調べて", ".md")]
    [InlineData("労基法の残業について調べてデスクトップにテキストファイルで保存して", true, "労基法の残業について調べて", ".txt")]
    [InlineData("hello world", false, "", "")]
    [InlineData("刑法第8条は？", false, "", "")]
    public void TryParse_DetectsDesktopExportIntent(string message, bool expected, string expectedQuery, string expectedExt)
    {
        var ok = ChatExportRequestParser.TryParse(message, out var request);
        Assert.Equal(expected, ok);
        if (!expected)
            return;

        Assert.Contains(expectedQuery, request.Query, StringComparison.Ordinal);
        Assert.Equal(expectedExt, request.Extension);
        Assert.Equal(ChatExportDestination.Desktop, request.Destination);
    }

    [Fact]
    public void TryParse_UsesQuotedFileName()
    {
        Assert.True(ChatExportRequestParser.TryParse(
            "「刑法まとめ.md」を作ってデスクトップに保存して",
            out var request));
        Assert.Equal("刑法まとめ", request.FileNameStem);
        Assert.Equal(".md", request.Extension);
    }
}
