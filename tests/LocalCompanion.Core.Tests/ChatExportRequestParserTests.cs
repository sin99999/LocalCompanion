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
    [InlineData("あの話を後で見返したいからファイルに残して", false, "", "")]
    [InlineData("大事なファイルは保存してね", false, "", "")]
    [InlineData("Wordファイルに保存してる？", false, "", "")]
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
    [InlineData("違法ダウンロードについて調べてデスクトップに置いて", ChatExportTargetKind.Desktop, "違法ダウンロード")]
    [InlineData("USBメモリの歴史を調べてデスクトップに保存して", ChatExportTargetKind.Desktop, "USBメモリ")]
    [InlineData("デスクトップアプリの話をUSBメモリに保存して", ChatExportTargetKind.RemovableStorage, "デスクトップアプリ")]
    public void TryParse_DetectsCustomDestinations(string message, ChatExportTargetKind kind, string expectedQuery)
    {
        Assert.True(ChatExportRequestParser.TryParse(message, out var request));
        Assert.Equal(kind, request.Target.Kind);
        Assert.Contains(expectedQuery, request.Query, StringComparison.Ordinal);
        Assert.Equal(ChatExportConflictPolicy.AskUser, request.ConflictPolicy);
    }

    [Fact]
    public void TryExtractExplicitDirectory_StripsParticleWithoutSpace()
    {
        var path = ChatExportRequestParser.TryExtractExplicitDirectory(
            @"結果をC:\work\exportsに保存して");
        Assert.NotNull(path);
        Assert.Equal(@"C:\work\exports", path, ignoreCase: true);
    }

    [Theory]
    [InlineData("性格をファイルに保存して", false)]
    [InlineData("エマ.jsonに書いて", false)]
    [InlineData("キャラ設定をデスクトップに保存して", true)]
    public void TryParse_PersonaUpdateWithoutDiskDestination_DoesNotExport(string message, bool expectedExport)
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(message));
        Assert.Equal(expectedExport, ChatExportRequestParser.TryParse(message, out _));
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
    public void TryExtractExplicitDirectory_AcceptsForwardSlashWindowsPath()
    {
        var path = ChatExportRequestParser.TryExtractExplicitDirectory(
            "メモを C:/work/exports に txt で保存して");
        Assert.NotNull(path);
        Assert.Equal(@"C:\work\exports", path, ignoreCase: true);
    }

    [Fact]
    public void TryParse_ForwardSlashPath_IsDirectoryTargetNotDesktop()
    {
        Assert.True(ChatExportRequestParser.TryParse(
            "刑法を調べて C:/work/exports に txt で保存して",
            out var request));
        Assert.Equal(ChatExportTargetKind.Directory, request.Target.Kind);
        Assert.Equal(@"C:\work\exports", request.Target.DirectoryPath, ignoreCase: true);
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
