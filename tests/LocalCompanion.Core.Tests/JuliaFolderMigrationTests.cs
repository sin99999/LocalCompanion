using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class JuliaFolderMigrationTests
{
    [Fact]
    public void Migrate_RenamesLayaFolderToJulia()
    {
        var root = NewRoot();
        try
        {
            var legacy = Path.Combine(root, "tools", "laya");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "hello.txt"), "julia");

            LayaSidecar.MigrateLegacyHome(root);

            var julia = Path.Combine(root, "tools", "Julia");
            Assert.Equal("julia", File.ReadAllText(Path.Combine(julia, "hello.txt")));
            Assert.False(Directory.Exists(legacy));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Migrate_WhenJuliaAlreadyExists_MovesMissingFilesAndRemovesLaya()
    {
        var root = NewRoot();
        try
        {
            var legacy = Path.Combine(root, "tools", "laya");
            var julia = Path.Combine(root, "tools", "Julia");
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(julia);
            File.WriteAllText(Path.Combine(julia, "keep.txt"), "stay");
            File.WriteAllText(Path.Combine(legacy, "keep.txt"), "old");
            File.WriteAllText(Path.Combine(legacy, "extra.txt"), "moved");

            LayaSidecar.MigrateLegacyHome(root);

            Assert.Equal("stay", File.ReadAllText(Path.Combine(julia, "keep.txt")));
            Assert.Equal("moved", File.ReadAllText(Path.Combine(julia, "extra.txt")));
            Assert.False(Directory.Exists(legacy));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Migrate_WhenLayaIsAbsent_LeavesJuliaAlone()
    {
        var root = NewRoot();
        try
        {
            var julia = Path.Combine(root, "tools", "Julia");
            Directory.CreateDirectory(julia);
            File.WriteAllText(Path.Combine(julia, "keep.txt"), "stay");

            LayaSidecar.MigrateLegacyHome(root);

            Assert.Equal("stay", File.ReadAllText(Path.Combine(julia, "keep.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "tools", "laya")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "lc-julia-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, true); } catch { /* ignore */ }
    }
}
