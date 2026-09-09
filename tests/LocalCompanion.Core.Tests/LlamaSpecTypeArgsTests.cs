using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaSpecTypeArgsTests
{
    [Fact]
    public void AppendDraftMtp_Enabled_AddsFlag()
    {
        var args = new List<string> { "-np", "1" };
        LlamaSpecTypeArgs.AppendDraftMtp(args, enabled: true);
        Assert.Equal(["-np", "1", "--spec-type", "draft-mtp"], args);
    }

    [Fact]
    public void AppendDraftMtp_Disabled_LeavesArgs()
    {
        var args = new List<string> { "-np", "1" };
        LlamaSpecTypeArgs.AppendDraftMtp(args, enabled: false);
        Assert.Equal(["-np", "1"], args);
    }

    [Fact]
    public void AppendDraftMtp_AlreadyPresent_DoesNotDuplicate()
    {
        var args = new List<string> { "--spec-type", "draft-mtp" };
        LlamaSpecTypeArgs.AppendDraftMtp(args, enabled: true);
        Assert.Equal(["--spec-type", "draft-mtp"], args);
    }
}
