using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportReplySanitizerTests
{
    [Fact]
    public void StripFakeSaveClaims_RemovesHallucinatedPath()
    {
        var input = """
                    刑法の要点をまとめました。

                    見てみてね😉👇デスクトップに保存しました: C:\Users\Example\Desktop\export-sample.txt
                    """;

        var cleaned = ChatExportReplySanitizer.StripFakeSaveClaims(input);
        Assert.DoesNotContain("デスクトップに保存しました", cleaned);
        Assert.Contains("刑法の要点", cleaned);
    }

    [Fact]
    public void StripFakeSaveClaims_RemovesOfficialLookingFakeLine()
    {
        var input = "本文です。\n\nデスクトップに保存しました: C:\\Users\\Example\\Desktop\\test.txt";
        var cleaned = ChatExportReplySanitizer.StripFakeSaveClaims(input);
        Assert.DoesNotContain("デスクトップに保存しました", cleaned);
        Assert.Contains("本文です", cleaned);
    }

    [Fact]
    public void StripFakeSaveClaims_PreservesLegitimateSaveMention()
    {
        var input = "メモ帳に保存しました。次に開いて確認してください。";
        var cleaned = ChatExportReplySanitizer.StripFakeSaveClaims(input);
        Assert.Contains("保存しました", cleaned);
    }
}
