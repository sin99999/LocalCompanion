using LocalCompanion;
using LocalCompanion.Services;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxInstallLocatorTests
{
    [Fact]
    public void DescribeInstall_HideInstallForTesting_ReturnsNotInstalled()
    {
        var locator = new VoicevoxInstallLocator(Options.Create(new VoicevoxOptions
        {
            HideInstallForTesting = true,
        }));

        var install = locator.DescribeInstall();

        Assert.False(install.Installed);
        Assert.Null(install.LauncherPath);
    }
}
