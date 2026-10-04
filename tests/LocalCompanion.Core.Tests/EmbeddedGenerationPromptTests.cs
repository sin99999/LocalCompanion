using System.IO.Compression;
using System.Text;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class EmbeddedGenerationPromptTests
{
    [Fact]
    public void PngParameters_KeepsNegativePrompt_DropsSteps()
    {
        var png = PngTextChunk(
            "parameters",
            "1girl, smile\nNegative prompt: lowres\nSteps: 20, Sampler: Euler a, CFG scale: 7");
        var text = EmbeddedGenerationPrompt.Read(Convert.ToBase64String(png));
        Assert.NotNull(text);
        Assert.Contains("1girl, smile", text);
        Assert.Contains("Negative prompt: lowres", text);
        Assert.DoesNotContain("Steps:", text);
    }

    [Fact]
    public void PngSoftwareChunk_IsIgnored()
    {
        var png = PngTextChunk("Software", "Automatic1111");
        Assert.Null(EmbeddedGenerationPrompt.Read(Convert.ToBase64String(png)));
    }

    [Fact]
    public void DataUrl_ReadsTheSameParameters()
    {
        var png = PngTextChunk("parameters", "masterpiece, best quality\nNegative prompt: blur");
        var url = "data:image/png;base64," + Convert.ToBase64String(png);
        var block = EmbeddedGenerationPrompt.FormatForChat([url]);
        Assert.NotNull(block);
        Assert.Contains("生成プロンプト", block);
        Assert.Contains("masterpiece, best quality", block);
        Assert.Contains("Negative prompt: blur", block);
    }

    [Fact]
    public void ComfyPromptJson_KeepsLongTextFields()
    {
        const string json =
            """
            {"3":{"class_type":"CLIPTextEncode","inputs":{"text":"1girl, silver hair"}},"9":{"inputs":{"text":"hi"}}}
            """;
        var png = PngTextChunk("prompt", json);
        var text = EmbeddedGenerationPrompt.Read(Convert.ToBase64String(png));
        Assert.Equal("1girl, silver hair", text);
    }

    [Fact]
    public void ZtxtParameters_Inflates()
    {
        var png = PngZtxtChunk("parameters", "1boy, coat\nNegative prompt: extra fingers\nSteps: 28");
        var text = EmbeddedGenerationPrompt.Read(Convert.ToBase64String(png));
        Assert.NotNull(text);
        Assert.Contains("1boy, coat", text);
        Assert.DoesNotContain("Steps:", text);
    }

    [Fact]
    public void JpegAsciiComment_KeepsPrompt_DropsSteps()
    {
        var body = Encoding.ASCII.GetBytes("1girl, smile\nNegative prompt: lowres\nSteps: 20, Sampler: Euler a");
        var jpeg = new byte[body.Length + 3];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        body.CopyTo(jpeg, 3);
        var text = EmbeddedGenerationPrompt.Read(Convert.ToBase64String(jpeg));
        Assert.NotNull(text);
        Assert.Contains("1girl, smile", text);
        Assert.Contains("Negative prompt: lowres", text);
        Assert.DoesNotContain("Steps:", text);
    }

    [Fact]
    public void JpegUtf16Comment_KeepsPrompt()
    {
        var body = Encoding.Unicode.GetBytes("1girl, smile\nNegative prompt: lowres\nSteps: 30");
        var text = EmbeddedGenerationPrompt.Read(Convert.ToBase64String(body));
        Assert.NotNull(text);
        Assert.Contains("1girl, smile", text);
        Assert.DoesNotContain("Steps:", text);
    }

    [Fact]
    public void PlainJpeg_ReturnsNull()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        Assert.Null(EmbeddedGenerationPrompt.Read(Convert.ToBase64String(jpeg)));
    }

    private static byte[] PngTextChunk(string keyword, string value)
    {
        var payload = Encoding.UTF8.GetBytes(keyword + "\0" + value);
        return PngChunk("tEXt", payload);
    }

    private static byte[] PngZtxtChunk(string keyword, string value)
    {
        var raw = Encoding.UTF8.GetBytes(value);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        var key = Encoding.ASCII.GetBytes(keyword);
        var payload = new byte[key.Length + 2 + compressed.Length];
        key.CopyTo(payload, 0);
        payload[key.Length + 1] = 0;
        compressed.ToArray().CopyTo(payload, key.Length + 2);
        return PngChunk("zTXt", payload);
    }

    private static byte[] PngChunk(string type, byte[] payload)
    {
        var chunk = new byte[12 + payload.Length];
        chunk[0] = (byte)((payload.Length >> 24) & 0xFF);
        chunk[1] = (byte)((payload.Length >> 16) & 0xFF);
        chunk[2] = (byte)((payload.Length >> 8) & 0xFF);
        chunk[3] = (byte)(payload.Length & 0xFF);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        var png = new byte[8 + chunk.Length];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        chunk.CopyTo(png, 8);
        return png;
    }
}
