namespace LocalCompanion.Core.Tests;

public sealed class LlamaDefaultModelTests
{
    [Fact]
    public void ChatDefaults_UseGoogleGemma4E2BQ4Repo()
    {
        Assert.Equal("gemma-4-E2B_q4_0-it.gguf", LlamaDefaultModel.ChatFileName);
        Assert.Contains("google/gemma-4-E2B-it-qat-q4_0-gguf", LlamaDefaultModel.ChatUrl);
        Assert.Equal("google/gemma-4-E2B-it-qat-q4_0-gguf", LlamaDefaultModel.HuggingFaceRepoId);
    }

    [Fact]
    public void MmprojDefaults_AlignWithChatRepo()
    {
        Assert.Equal("gemma-4-E2B-it-mmproj.gguf", LlamaDefaultModel.MmprojFileName);
        Assert.Contains("gemma-4-E2B-it-mmproj.gguf", LlamaDefaultModel.MmprojUrl);
        Assert.Contains("google/gemma-4-E2B-it-qat-q4_0-gguf", LlamaDefaultModel.MmprojUrl);
    }

    [Theory]
    [InlineData("gemma-4-E2B-it-mmproj.gguf", true)]
    [InlineData("mmproj-BF16.gguf", true)]
    [InlineData("mmproj-F16.gguf", true)]
    [InlineData("other-mmproj.gguf", false)]
    public void IsE2BMmprojFileName_RecognizesExpectedNames(string fileName, bool expected)
    {
        Assert.Equal(expected, LlamaDefaultModel.IsE2BMmprojFileName(fileName));
    }
}
