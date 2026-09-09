using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaCppLaunchFilesTests
{
    [Fact]
    public void HasRequiredServerFiles_StubWithoutImpl_False()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "llama-server.exe"), new byte[9216]);
            Assert.False(LlamaCppLaunchFiles.HasRequiredServerFiles(dir));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void HasRequiredServerFiles_StubWithImpl_True()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "llama-server.exe"), new byte[9216]);
            File.WriteAllBytes(Path.Combine(dir, "llama-server-impl.dll"), new byte[64]);
            Assert.True(LlamaCppLaunchFiles.HasRequiredServerFiles(dir));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void HasRequiredServerFiles_FatExeWithoutImpl_True()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllBytes(
                Path.Combine(dir, "llama-server.exe"),
                new byte[LlamaCppLaunchFiles.StubExeMaxBytes + 1]);
            Assert.True(LlamaCppLaunchFiles.HasRequiredServerFiles(dir));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Theory]
    [InlineData(unchecked((int)0xC0000135), true)]
    [InlineData(unchecked((int)0xC0000906), true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsWindowsLoaderFailure_Codes(int exitCode, bool expected)
    {
        Assert.Equal(expected, LlamaCppLaunchFiles.IsWindowsLoaderFailure(exitCode));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}
