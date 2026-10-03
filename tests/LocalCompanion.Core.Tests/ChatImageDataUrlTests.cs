using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatImageDataUrlTests
{
    [Fact]
    public void ToDataUrl_PngMagic_IsNotLabeledJpeg()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var url = ChatAttachmentFileRules.ToDataUrl(png);
        Assert.StartsWith("data:image/png;base64,", url);
    }

    [Fact]
    public void ToDataUrl_WebpMagic_KeepsWebp()
    {
        var webp = new byte[12];
        webp[0] = (byte)'R';
        webp[1] = (byte)'I';
        webp[2] = (byte)'F';
        webp[3] = (byte)'F';
        webp[8] = (byte)'W';
        webp[9] = (byte)'E';
        webp[10] = (byte)'B';
        webp[11] = (byte)'P';
        var url = ChatAttachmentFileRules.ToDataUrl(Convert.ToBase64String(webp));
        Assert.StartsWith("data:image/webp;base64,", url);
    }

    [Fact]
    public void ToDataUrl_GifAndBmpAndJpeg_UseTheirOwnTypes()
    {
        Assert.StartsWith("data:image/gif;base64,", ChatAttachmentFileRules.ToDataUrl("GIF89a"u8));
        Assert.StartsWith("data:image/bmp;base64,", ChatAttachmentFileRules.ToDataUrl("BM"u8));
        Assert.StartsWith("data:image/jpeg;base64,", ChatAttachmentFileRules.ToDataUrl(new byte[] { 0xFF, 0xD8, 0xFF }));
    }

    [Fact]
    public void ToDataUrl_ExistingDataUrl_StaysUntouched()
    {
        const string url = "data:image/png;base64,aaaa";
        Assert.Equal(url, ChatAttachmentFileRules.ToDataUrl(url));
    }
}
