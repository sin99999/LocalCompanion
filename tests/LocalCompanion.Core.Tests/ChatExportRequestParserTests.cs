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
    [InlineData("今回のビッグマック指数について会話の内容をデスクトップにテキストファイルで書きだしてくれる？", true, "ビッグマック指数", ".txt")]
    [InlineData("会話の内容をデスクトップにテキストファイルで書き出して", true, "会話", ".txt")]
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
        Assert.Equal(ChatExportConflictPolicy.AskUser, request.ConflictPolicy);
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
    [InlineData("刑法を調べて C:\\work\\exports に txt で保存して", ChatExportTargetKind.Directory, "刑法を調べて")]
    [InlineData("結果をUSBメモリに保存して", ChatExportTargetKind.RemovableStorage, "結果を")]
    [InlineData("まとめをドキュメントに保存して", ChatExportTargetKind.Documents, "まとめを")]
    [InlineData("内容をダウンロードフォルダに保存して", ChatExportTargetKind.Downloads, "内容を")]
    public void TryParse_DetectsCustomDestinations(string message, ChatExportTargetKind kind, string expectedQuery)
    {
        Assert.True(ChatExportRequestParser.TryParse(message, out var request));
        Assert.Equal(kind, request.Target.Kind);
        Assert.Contains(expectedQuery, request.Query, StringComparison.Ordinal);
        Assert.Equal(ChatExportConflictPolicy.AskUser, request.ConflictPolicy);
    }

    [Theory]
    [InlineData("このファイルを上書きして保存して", ChatExportConflictPolicy.Overwrite)]
    [InlineData("このファイルを同じ場所に別名保存して", ChatExportConflictPolicy.SaveAsNewFile)]
    [InlineData("このファイルを保存して、上書きやっちゃって", ChatExportConflictPolicy.Overwrite)]
    [InlineData("このファイルを保存して、別名だけで", ChatExportConflictPolicy.SaveAsNewFile)]
    public void TryParse_DetectsConflictPolicy(string message, ChatExportConflictPolicy expected)
    {
        Assert.True(ChatExportRequestParser.TryParse(message, out var request));
        Assert.Equal(expected, request.ConflictPolicy);
    }

    [Fact]
    public void TryExtractExplicitDirectory_UsesLongestWindowsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-export-path-test");
        var path = ChatExportRequestParser.TryExtractExplicitDirectory(
            $"{dir} に保存して");
        Assert.NotNull(path);
        Assert.StartsWith(dir, path, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(ChatExportConflictPolicy.AskUser, request.ConflictPolicy);
    }

    [Fact]
    public void TryInheritRepeatExport_OverridesConflictPolicyByFollowUp()
    {
        var prior = new[] { "刑法を調べて結果をデスクトップにtxtで保存して" };
        var ok = ChatExportRequestParser.TryInheritRepeatExport(
            "上書きして",
            prior,
            out var request);

        Assert.True(ok);
        Assert.Equal(ChatExportConflictPolicy.Overwrite, request.ConflictPolicy);
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

    [Fact]
    public void TryParseConflictResolution_IgnoresCasualNamingQuestion()
    {
        Assert.False(ChatExportRequestParser.TryParseConflictResolution(
            "この概念に名前を付けて説明して",
            out _));
    }

    [Theory]
    [InlineData("上書き保存")]
    [InlineData("別名だけ")]
    [InlineData("save as")]
    public void TryParseConflictResolution_DetectsShortReplies(string message)
    {
        Assert.True(ChatExportRequestParser.TryParseConflictResolution(message, out var policy));
        Assert.NotEqual(ChatExportConflictPolicy.AskUser, policy);
    }
}
