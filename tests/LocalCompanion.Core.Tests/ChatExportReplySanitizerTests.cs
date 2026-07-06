using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportReplySanitizerTests
{
    [Fact]
    public void StripFakeSaveClaims_RemovesHallucinatedPath()
    {
        var input = """
                    刑法の要点をまとめました。

                    見てみてね😉👇デスクトップに保存しました: C:\Users\SIN\Desktop\再処理済み_レンの気持ちと刑法抜粋.txt
                    """;

        var cleaned = ChatExportReplySanitizer.StripFakeSaveClaims(input);
        Assert.DoesNotContain("デスクトップに保存しました", cleaned);
        Assert.Contains("刑法の要点", cleaned);
    }
}
