using System.Text.Json;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class FirstRunModelSetupTests
{
    [Fact]
    public void NeedsSetup_ReturnsFalse_WhenAlreadyComplete()
    {
        var root = CreateTempDir();
        try
        {
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            ModelLibrarySettings.SaveDocument(dataDir, new ModelLibrarySettings.ModelLibraryDocument(null, true));

            Assert.False(FirstRunModelSetup.NeedsSetup(dataDir, modelsDir));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void NeedsSetup_AutoCompletes_WhenBootstrapMarkerExists()
    {
        var root = CreateTempDir();
        try
        {
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            File.WriteAllText(Path.Combine(modelsDir, ".default-model-bootstrap.json"), "{}");

            Assert.False(FirstRunModelSetup.NeedsSetup(dataDir, modelsDir));
            Assert.True(ModelLibrarySettings.TryLoadDocument(dataDir)?.FirstRunSetupComplete);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryCompleteExternalFolder_PicksSmallestChatGguf()
    {
        var root = CreateTempDir();
        try
        {
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(userModels);
            File.WriteAllText(Path.Combine(userModels, "big-12b.gguf"), new string('x', 12_000));
            File.WriteAllText(Path.Combine(userModels, "small-2b.gguf"), new string('x', 2_000));

            var ok = FirstRunModelSetup.TryCompleteExternalFolder(modelsDir, dataDir, userModels, out _);

            Assert.True(ok);
            var json = File.ReadAllText(Path.Combine(modelsDir, "selection.json"));
            Assert.Contains("small-2b.gguf", json);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryCompleteExternalFolder_SavesFolderAndSkipsDefaultDownload()
    {
        var root = CreateTempDir();
        try
        {
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(userModels);
            File.WriteAllText(Path.Combine(userModels, "my-chat.gguf"), "fake");

            var ok = FirstRunModelSetup.TryCompleteExternalFolder(modelsDir, dataDir, userModels, out var errorKey);

            Assert.True(ok);
            Assert.Null(errorKey);
            Assert.Equal(userModels, ModelLibrarySettings.LoadAdditionalFolder(dataDir));
            Assert.True(File.Exists(Path.Combine(modelsDir, "selection.json")));
            Assert.True(File.Exists(Path.Combine(modelsDir, ".default-model-bootstrap.json")));
            Assert.True(ModelLibrarySettings.TryLoadDocument(dataDir)?.FirstRunSetupComplete);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryCompleteExternalFolder_Fails_WhenNoChatGguf()
    {
        var root = CreateTempDir();
        try
        {
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(userModels);
            File.WriteAllText(Path.Combine(userModels, "mmproj-test.gguf"), "fake");

            var ok = FirstRunModelSetup.TryCompleteExternalFolder(modelsDir, dataDir, userModels, out var errorKey);

            Assert.False(ok);
            Assert.Equal("FirstRun.Setup.Error.NoGguf", errorKey);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryCompleteExternalFolder_SavesMmprojFullPath_WhenBesideChatModel()
    {
        var root = CreateTempDir();
        try
        {
            SeedRepoConfig(root);
            var dataDir = Path.Combine(root, "data");
            var modelsDir = Path.Combine(root, "models");
            var userModels = Path.Combine(root, "user-models");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(userModels);
            File.WriteAllText(Path.Combine(userModels, "gemma-4-E2B_q4_0-it.gguf"), "chat");
            File.WriteAllText(Path.Combine(userModels, "gemma-4-E2B-it-mmproj.gguf"), "mmproj");

            var ok = FirstRunModelSetup.TryCompleteExternalFolder(modelsDir, dataDir, userModels, out _);

            Assert.True(ok);
            var dto = JsonSerializer.Deserialize<ModelSelectionDto>(
                File.ReadAllText(Path.Combine(modelsDir, "selection.json")));
            Assert.Equal("gemma-4-E2B-it-mmproj.gguf", dto?.MmprojFileName);
            Assert.Contains("user-models", dto?.MmprojFullPath ?? "", StringComparison.OrdinalIgnoreCase);
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
        Path.Combine(Path.GetTempPath(), "lc-first-run-" + Guid.NewGuid().ToString("N"));

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
