using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportRequestParserTests
{
    [Theory]
    [InlineData("刑法第54条を調べて結果をデスクトップにmdで置いといて", true, "刑法第54条を調べて", ".md")]
    [InlineData("労基法の残業について調べてデスクトップにテキストファイルで保存して", true, "労基法の残業について調べて", ".txt")]
    [InlineData("刑法を調べて結果をtxtで書いておいて", true, "刑法を調べて", ".txt")]
    [InlineData("内容をファイルに保存して", true, "内容を", ".md")]
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
        Assert.Equal(ChatExportTargetKind.Desktop, request.Target.Kind);
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

    [Theory]
    [InlineData("刑法を調べて H:\\work\\exports に txt で保存して", ChatExportTargetKind.Directory, "刑法を調べて")]
    [InlineData("結果をUSBメモリに保存して", ChatExportTargetKind.RemovableStorage, "結果を")]
    [InlineData("まとめをドキュメントに保存して", ChatExportTargetKind.Documents, "まとめを")]
    [InlineData("内容をダウンロードフォルダに保存して", ChatExportTargetKind.Downloads, "内容を")]
    public void TryParse_DetectsCustomDestinations(string message, ChatExportTargetKind kind, string expectedQuery)
    {
        Assert.True(ChatExportRequestParser.TryParse(message, out var request));
        Assert.Equal(kind, request.Target.Kind);
        Assert.Contains(expectedQuery, request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractExplicitDirectory_UsesLongestWindowsPath()
    {
        var path = ChatExportRequestParser.TryExtractExplicitDirectory(
            "H:\\pg\\Cursor\\out に保存して");
        Assert.NotNull(path);
        Assert.StartsWith("H:\\pg\\Cursor\\out", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryInheritRepeatExport_InheritsPriorDesktopRequest()
    {
        var prior = new[] { "刑法を調べて結果をデスクトップにtxtで保存して" };
        var ok = ChatExportRequestParser.TryInheritRepeatExport(
            "今の処理をもう一度お願い。何度もごめんねテストだから付き合って？",
            prior,
            out var request);

        Assert.True(ok);
        Assert.Contains("刑法を調べて", request.Query, StringComparison.Ordinal);
        Assert.Equal(".txt", request.Extension);
    }

    [Fact]
    public void TryInheritRepeatExport_SkipsWhenNoPriorExport()
    {
        var prior = new[] { "刑法第8条は？" };
        var ok = ChatExportRequestParser.TryInheritRepeatExport(
            "今の処理をもう一度お願い",
            prior,
            out _);

        Assert.False(ok);
    }
}
