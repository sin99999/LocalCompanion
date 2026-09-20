using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatCopyableBlobDetectorTests
{
    [Fact]
    public void IsCopyableBlob_StableDiffusionTagList_IsTrue()
    {
        const string prompt =
            "(Masterpiece, Bestquality,Highres:1.2),(Anime style:1.3),1girl, nagato yuki, "
            + "defensive pose, looking at camera, determined expression, serious face, "
            + "short navy blue hair, purple eyes, floating in void, cosmic background, "
            + "glitch effect, digital debris, data fragments, glowing particles";

        Assert.True(ChatCopyableBlobDetector.IsCopyableBlob(prompt));
    }

    [Fact]
    public void IsCopyableBlob_NegativePromptLabel_IsTrue()
    {
        Assert.True(ChatCopyableBlobDetector.IsCopyableBlob(
            "(Negative Prompt: lowres, bad anatomy, bad hands, extra digits, jpeg artifacts)"));
    }

    [Fact]
    public void IsCopyableBlob_ImagineCommand_IsTrue()
    {
        Assert.True(ChatCopyableBlobDetector.IsCopyableBlob("/imagine a cat sitting on a roof --ar 16:9"));
    }

    [Fact]
    public void IsCopyableBlob_JapaneseSentence_IsFalse()
    {
        Assert.False(ChatCopyableBlobDetector.IsCopyableBlob(
            "生成されたプロンプトは、アニメーションスタイルで防衛的なエネルギーを強調するように構成した。"));
    }

    [Fact]
    public void IsCopyableBlob_MarkdownTableRow_IsFalse()
    {
        Assert.False(ChatCopyableBlobDetector.IsCopyableBlob(
            "| 行 | 列 | 中身（例文） | 主体 | 1 | 服装／対象名 | 2 | 髪型・色 | 3 | 表情 | 4 | 姿勢 | 5 | 背景環境 | 6 | 照明 |"));
    }

    [Fact]
    public void IsCopyableBlob_DotnetCommand_IsTrue()
    {
        Assert.True(ChatCopyableBlobDetector.IsCopyableBlob(
            "dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64"));
    }

    [Fact]
    public void IsCopyableBlob_PowerShellFileInvoke_IsTrue()
    {
        Assert.True(ChatCopyableBlobDetector.IsCopyableBlob(
            "powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\run-debug-winui.ps1"));
    }

    [Fact]
    public void IsCopyableBlob_GitTalkProse_IsFalse()
    {
        Assert.False(ChatCopyableBlobDetector.IsCopyableBlob("git の話をしよう"));
    }

    [Fact]
    public void TextForDisplay_UnwrapsCornerQuotes()
    {
        var inner = ChatCopyableBlobDetector.TextForDisplay("「1girl, solo, smile, looking at viewer」");
        Assert.Equal("1girl, solo, smile, looking at viewer", inner);
    }

    [Fact]
    public void TextForDisplay_StripsLeadingCommaOnTagList()
    {
        var text = ChatCopyableBlobDetector.TextForDisplay(
            ", bad anatomy, text, error, extra digit, low quality, blurry, messy environment, distorted hands, weird fingers");
        Assert.StartsWith("bad anatomy", text, StringComparison.Ordinal);
        Assert.False(text.StartsWith(','));
    }

    [Fact]
    public void TextForDisplay_StripsCommaAfterNegativeLabel()
    {
        var text = ChatCopyableBlobDetector.TextForDisplay(
            "(Negative Prompt: , bad anatomy, extra digits, jpeg artifacts, lowres, blurry)");
        Assert.Contains("Negative Prompt:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompt: ,", text, StringComparison.Ordinal);
        Assert.Contains("bad anatomy", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Segment_PeelsJapaneseCommentFromTagList()
    {
        const string line =
            "beautiful Japanese woman, silk kimono, seiza, tea ceremony, masterpiece, best quality, highly detailed, 8k wallpaper "
            + "しんや、これで試してみて！ , bad anatomy, text, error, extra digit, low quality, blurry, messy environment, distorted hands";

        var parts = ChatCopyableBlobDetector.Segment(line);
        Assert.Contains(parts, p => p.IsBlob && p.Text.Contains("8k wallpaper", StringComparison.Ordinal));
        Assert.Contains(parts, p => !p.IsBlob && p.Text.Contains("試してみて", StringComparison.Ordinal));
        Assert.Contains(parts, p => p.IsBlob && p.Text.Contains("bad anatomy", StringComparison.Ordinal));
        Assert.DoesNotContain(parts, p => p.IsBlob && p.Text.Contains("試してみて", StringComparison.Ordinal));
        Assert.DoesNotContain(parts, p => p.IsBlob && p.Text.StartsWith(','));
    }

    [Fact]
    public void Segment_JapaneseParenAside_StaysOneNonBlob()
    {
        const string line =
            "着物を着た女子高生(氷菓のエルたそみたいな？)と雪の積もる初詣に出かけるシチュをSDプロンプトで出力して？";

        var parts = ChatCopyableBlobDetector.Segment(line);
        var prose = Assert.Single(parts);
        Assert.False(prose.IsBlob);
        Assert.Equal(line, prose.Text);
    }

    [Fact]
    public void Segment_KeepsShortJapaneseTagsInBlob()
    {
        const string line =
            "1girl, solo, 女の子, 着物, smile, looking at viewer, long hair, school uniform, sitting, outdoors, cherry blossoms";

        var parts = ChatCopyableBlobDetector.Segment(line);
        var blob = Assert.Single(parts);
        Assert.True(blob.IsBlob);
        Assert.Contains("着物", blob.Text, StringComparison.Ordinal);
    }
}
