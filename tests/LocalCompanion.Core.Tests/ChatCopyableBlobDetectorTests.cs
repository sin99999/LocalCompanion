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
    public void TextForDisplay_UnwrapsCornerQuotes()
    {
        var inner = ChatCopyableBlobDetector.TextForDisplay("「1girl, solo, smile, looking at viewer」");
        Assert.Equal("1girl, solo, smile, looking at viewer", inner);
    }
}
