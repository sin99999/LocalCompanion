using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class AppUpdateServiceTests
{
    [Theory]
    [InlineData("v1.0.2", "1.0.2")]
    [InlineData("1.0.1", "1.0.1")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void ParseReleaseTag_NormalizesGitHubTag(string tag, string expected)
    {
        Assert.Equal(expected, AppUpdateService.ParseReleaseTag(tag));
    }

    [Fact]
    public void ParseReleaseTag_ReturnsNullForEmpty()
    {
        Assert.Null(AppUpdateService.ParseReleaseTag(null));
        Assert.Null(AppUpdateService.ParseReleaseTag(""));
    }
}
