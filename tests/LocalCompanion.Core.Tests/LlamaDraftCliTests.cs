using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaDraftCliTests
{
    private const string NewHelp = """
        --spec-draft-n-max N                    number of tokens to draft for speculative decoding (default: 3)
        --spec-draft-model, -md, --model-draft FNAME
                                                draft model for speculative decoding (default: unused)
        --draft, --draft-n, --draft-max N       the argument has been removed. use --spec-draft-n-max or
                                                --spec-ngram-mod-n-max
        """;

    private const string LegacyHelp = """
        -md, --model-draft FNAME                draft model for speculative decoding
        --draft-max N                           number of tokens to draft (default: 16)
        """;

    [Fact]
    public void ParseHelp_NewLlama_UsesSpecDraftNMax()
    {
        var support = LlamaDraftCli.ParseHelp(NewHelp);

        Assert.True(support.CanAttachModel);
        Assert.Equal(LlamaDraftMaxFlag.SpecNMax, support.MaxFlag);
        Assert.Equal("--spec-draft-n-max", support.MaxTokensArg);
    }

    [Fact]
    public void ParseHelp_LegacyLlama_UsesDraftMax()
    {
        var support = LlamaDraftCli.ParseHelp(LegacyHelp);

        Assert.True(support.CanAttachModel);
        Assert.Equal(LlamaDraftMaxFlag.LegacyDraftMax, support.MaxFlag);
        Assert.Equal("--draft-max", support.MaxTokensArg);
    }

    [Fact]
    public void ParseHelp_Empty_IsNone()
    {
        Assert.Equal(LlamaDraftCliSupport.None, LlamaDraftCli.ParseHelp(""));
        Assert.Equal(LlamaDraftCliSupport.None, LlamaDraftCli.ParseHelp("no options here"));
    }

    [Fact]
    public void LooksLikeCliArgRejected_MatchesRemovedFlag()
    {
        Assert.True(LlamaDraftCli.LooksLikeCliArgRejected(
            "error while handling argument \"--draft-max\": the argument has been removed."));
        Assert.False(LlamaDraftCli.LooksLikeCliArgRejected("slot update_slots: all slots are idle"));
    }

    [Fact]
    public void RemoveDraftArgs_DropsModelAndMax()
    {
        var args = new List<string>
        {
            "-m", "main.gguf",
            "-md", "draft.gguf",
            "--spec-draft-n-max", "8",
            "-c", "4096",
        };

        Assert.True(LlamaDraftCli.RemoveDraftArgs(args));
        Assert.Equal(["-m", "main.gguf", "-c", "4096"], args);
        Assert.False(LlamaDraftCli.RemoveDraftArgs(args));
    }
}
