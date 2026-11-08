using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxUpdateServiceTests
{
    [Theory]
    [InlineData("v0.19.0", "0.19.0")]
    [InlineData("\"0.20.1\"", "0.20.1")]
    [InlineData("0.21.0-beta.1", "0.21.0")]
    public void NormalizeVersion_StripsPrefixAndSuffix(string raw, string expected)
    {
        Assert.Equal(expected, VoicevoxUpdateService.NormalizeVersion(raw));
    }

    [Theory]
    [InlineData("0.20.0", "0.19.0", true)]
    [InlineData("0.19.0", "0.20.0", false)]
    [InlineData("1.0.0", null, true)]
    [InlineData(null, "1.0.0", false)]
    public void IsNewerVersion_ComparesSemver(string? latest, string? current, bool expected)
    {
        Assert.Equal(expected, VoicevoxUpdateService.IsNewerVersion(latest, current));
    }

    [Theory]
    [InlineData("0.26.2", "0.26.1", "0.26.2")]
    [InlineData("0.26.2.0", "0.26.0", "0.26.2.0")]
    [InlineData(null, "0.26.1", "0.26.1")]
    [InlineData("", "0.26.1", "0.26.1")]
    public void SelectInstalledVersion_PrefersTheEditor(string? editor, string? engine, string expected)
    {
        Assert.Equal(expected, VoicevoxUpdateService.SelectInstalledVersion(editor, engine));
    }

    [Theory]
    [InlineData("0.26.2", "0.26.2", true)]
    [InlineData("0.26.2", "0.26.2.0", true)]
    [InlineData("0.26.2", "0.26.1", false)]
    [InlineData("0.26.2", null, false)]
    public void IsReleaseInstalled_TreatsTheEditorVersionAsCurrent(string? target, string? installed, bool expected)
    {
        Assert.Equal(expected, VoicevoxUpdateService.IsReleaseInstalled(target, installed));
    }
}
