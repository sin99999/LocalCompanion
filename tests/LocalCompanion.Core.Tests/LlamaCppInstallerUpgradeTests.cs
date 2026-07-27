using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaCppInstallerUpgradeTests
{
    [Theory]
    [InlineData(null, "b6500", true)]
    [InlineData("", "b6500", true)]
    [InlineData("b6400", "b6500", true)]
    [InlineData("b6500", "b6500", false)]
    [InlineData("B6500", "b6500", false)]
    [InlineData("b6500", null, false)]
    [InlineData("b6500", "", false)]
    public void NeedsLatestReleaseUpgrade_Scenarios(string? installed, string? latest, bool expected)
    {
        Assert.Equal(expected, LlamaCppInstaller.NeedsLatestReleaseUpgrade(installed, latest));
    }
}
