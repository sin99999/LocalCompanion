using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaMtpSupportTests
{
    [Theory]
    [InlineData("Huihui-Qwen3.8-27B-ablitrated-UD-IQ2_S-MTP.gguf", true)]
    [InlineData("something_mtp_q4_k.gguf", true)]
    [InlineData("gemma-4-E2B-it-Q4_0.gguf", false)]
    [InlineData("gemma-4-E4B-it-Q4_0.gguf", false)]
    [InlineData("gemma-3-26B-A4B-it-Q4_0.gguf", false)]
    [InlineData("model-SMTP.gguf", false)]
    [InlineData("", false)]
    public void LooksCapable_UsesMtpNameToken(string fileName, bool expected)
    {
        Assert.Equal(expected, LlamaMtpSupport.LooksCapable(fileName));
    }

    [Fact]
    public void ShouldEnable_UserOnButNotCapable_False()
    {
        Assert.False(LlamaMtpSupport.ShouldEnable(true, "gemma-4-E2B-it-Q4_0.gguf"));
    }

    [Fact]
    public void ShouldEnable_UserOnAndCapable_True()
    {
        Assert.True(LlamaMtpSupport.ShouldEnable(true, "Huihui-Qwen3.8-27B-ablitrated-UD-IQ2_S-MTP.gguf"));
    }

    [Fact]
    public void ShouldEnable_UserOffAndCapable_False()
    {
        Assert.False(LlamaMtpSupport.ShouldEnable(false, "Huihui-Qwen3.8-27B-ablitrated-UD-IQ2_S-MTP.gguf"));
    }
}
