using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class MmprojSupportTests
{
    [Fact]
    public void NeedsKnownMmprojDownload_ReturnsFalse_WhenMmprojInExternalFolder()
    {
        var root = CreateTempDir();
        try
        {
            SeedRepoConfig(root);
            var modelsDir = Path.Combine(root, "models");
            var dataDir = Path.Combine(root, "data");
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(dataDir);

            var chatPath = Path.Combine(userModels, "gemma-4-E2B_q4_0-it.gguf");
            var mmprojPath = Path.Combine(userModels, "gemma-4-E2B-it-mmproj.gguf");
            Directory.CreateDirectory(userModels);
            File.WriteAllText(chatPath, "chat");
            File.WriteAllText(mmprojPath, "mmproj");

            Assert.False(MmprojSupport.NeedsKnownMmprojDownload(
                root,
                "gemma-4-E2B_q4_0-it.gguf",
                chatPath));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryFindMmprojForModel_FindsMmprojBesideExternalChatModel()
    {
        var root = CreateTempDir();
        try
        {
            SeedRepoConfig(root);
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(userModels);

            var chatPath = Path.Combine(userModels, "gemma-4-E2B_q4_0-it.gguf");
            var mmprojPath = Path.Combine(userModels, "gemma-4-E2B-it-mmproj.gguf");
            File.WriteAllText(chatPath, "chat");
            File.WriteAllText(mmprojPath, "mmproj");

            var found = MmprojSupport.TryFindMmprojForModel(
                root,
                "gemma-4-E2B_q4_0-it.gguf",
                chatPath);

            Assert.NotNull(found);
            Assert.Equal(mmprojPath, found!.FullName);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static void SeedRepoConfig(string root)
    {
        var repoRoot = FindRepoRoot();
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);
        File.Copy(
            Path.Combine(repoRoot, "config", "mmproj-families.json"),
            Path.Combine(configDir, "mmproj-families.json"));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "config", "mmproj-families.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string CreateTempDir() =>
        Path.Combine(Path.GetTempPath(), "lc-mmproj-" + Guid.NewGuid().ToString("N"));

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }
}
