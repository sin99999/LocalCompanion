using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void WriteAllText_CreatesFileWithContent()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            AtomicFile.WriteAllText(path, """{"ok":true}""");

            Assert.True(File.Exists(path));
            Assert.Equal("""{"ok":true}""", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFile()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "nested", "a.json");
            AtomicFile.WriteAllText(path, "v1");
            AtomicFile.WriteAllText(path, "v2");

            Assert.Equal("v2", File.ReadAllText(path));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}
