using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class MmprojHuggingFaceDiscoveryTests
{
    private const string HuihuiChat = "Huihui-Qwen3.8-27B-abliterated-UD-IQ4_XS.gguf";
    private const string HuihuiRepo = "huihui-ai/Huihui-Qwen3.8-27B-abliterated-GGUF";

    [Fact]
    public void NormalizeModelStem_StripsUdIqQuant()
    {
        Assert.Equal(
            "Huihui-Qwen3.8-27B-abliterated",
            MmprojFamilyRegistry.NormalizeModelStem(HuihuiChat));
        Assert.Equal(
            "Huihui-Qwen3.8-27B-abliterated",
            MmprojFamilyRegistry.NormalizeModelStem(
                "Huihui-Qwen3.8-27B-abliterated-UD-DW-Q4_K_M.gguf"));
    }

    [Fact]
    public void GetHfSearchTerms_PutsStemGgufBeforeFullQuantName()
    {
        var root = CreateEmptyRegistryRoot();
        try
        {
            var terms = MmprojFamilyRegistry.GetHfSearchTerms(HuihuiChat, root).ToList();
            Assert.Contains("Huihui-Qwen3.8-27B-abliterated-GGUF", terms, StringComparer.OrdinalIgnoreCase);
            Assert.True(
                terms.IndexOf("Huihui-Qwen3.8-27B-abliterated-GGUF")
                < terms.IndexOf("Huihui-Qwen3.8-27B-abliterated-UD-IQ4_XS"));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void ScoreRepoId_PrefersExactStemGgufRepoOverFork()
    {
        var official = MmprojHuggingFaceDiscovery.ScoreRepoId(
            HuihuiRepo,
            HuihuiChat,
            []);
        var fork = MmprojHuggingFaceDiscovery.ScoreRepoId(
            "augustine223/Huihui-Qwen3.8-27B-abliterated-KO-i1-GGUF",
            HuihuiChat,
            []);
        Assert.True(official > fork);
    }

    [Fact]
    public void TryPickFromListing_UnknownFamily_RequiresExactChatFile()
    {
        var siblings = new[]
        {
            "Huihui-Qwen3.8-27B-abliterated-Q4_K.gguf",
            "mmproj-model-bf16.gguf",
        };

        Assert.Null(MmprojHuggingFaceDiscovery.TryPickFromListing(
            HuihuiChat,
            HuihuiRepo,
            siblings,
            matched: null));
    }

    [Fact]
    public void TryPickFromListing_SameRepoListing_PicksGenericMmproj()
    {
        var siblings = new[]
        {
            HuihuiChat,
            "Huihui-Qwen3.8-27B-abliterated-Q4_K.gguf",
            "README.md",
            "mmproj-model-bf16.gguf",
        };

        var hit = MmprojHuggingFaceDiscovery.TryPickFromListing(
            HuihuiChat,
            HuihuiRepo,
            siblings,
            matched: null);

        Assert.NotNull(hit);
        Assert.Equal("mmproj-model-bf16.gguf", hit!.Value.RemoteRelativePath);
        Assert.Contains("Huihui-Qwen3.8-27B-abliterated", hit.Value.LocalName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mmproj-model-bf16", hit.Value.LocalName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsKnownMmprojDownload_ProbesUnknownLongName()
    {
        var root = CreateEmptyRegistryRoot();
        try
        {
            Assert.True(MmprojSupport.NeedsKnownMmprojDownload(root, HuihuiChat));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void NeedsKnownMmprojDownload_SkipsShortGenericName()
    {
        var root = CreateEmptyRegistryRoot();
        try
        {
            Assert.False(MmprojSupport.NeedsKnownMmprojDownload(root, "tiny.gguf"));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static string CreateEmptyRegistryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lc-mmproj-hf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.WriteAllText(
            Path.Combine(root, "config", "mmproj-families.json"),
            """{"version":1,"repoPriorityPrefixes":[],"families":[],"genericVisionPatterns":[]}""");
        return root;
    }

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
