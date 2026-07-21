using System.IO.Compression;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class UserDataBackupTests
{
    [Fact]
    public void ExportToZip_ReplacesExistingWithoutLeavingPartialAsFinal()
    {
        var dataDir = CreateTempDir("lc-backup-data-");
        var zipPath = Path.Combine(Path.GetTempPath(), "lc-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllText(Path.Combine(dataDir, "a.txt"), "first");
            Assert.Equal(1, UserDataBackup.ExportToZip(dataDir, zipPath));
            Assert.True(File.Exists(zipPath));

            File.WriteAllText(Path.Combine(dataDir, "b.txt"), "second");
            Assert.Equal(2, UserDataBackup.ExportToZip(dataDir, zipPath));

            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Contains(zip.Entries, e => e.FullName is "a.txt");
            Assert.Contains(zip.Entries, e => e.FullName is "b.txt");

            var leftovers = Directory.GetFiles(Path.GetDirectoryName(zipPath)!, Path.GetFileName(zipPath) + "*.partial.zip");
            Assert.Empty(leftovers);
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDir(dataDir);
        }
    }

    [Fact]
    public void ExportToZip_OnFailure_KeepsPreviousZip()
    {
        var dataDir = CreateTempDir("lc-backup-data-");
        var zipPath = Path.Combine(Path.GetTempPath(), "lc-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllText(Path.Combine(dataDir, "keep.txt"), "safe");
            Assert.Equal(1, UserDataBackup.ExportToZip(dataDir, zipPath));
            var previousBytes = File.ReadAllBytes(zipPath);

            // 存在しないサブパスを beforeCopy でわざと失敗させる
            Assert.ThrowsAny<Exception>(() =>
                UserDataBackup.ExportToZip(dataDir, zipPath, beforeCopyFile: _ =>
                    throw new IOException("simulated copy failure")));

            Assert.True(File.Exists(zipPath));
            Assert.Equal(previousBytes, File.ReadAllBytes(zipPath));
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDir(dataDir);
        }
    }

    private static string CreateTempDir(string prefix) =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
