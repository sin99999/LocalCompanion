using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class SettingsTabCatalogTests
{
    [Fact]
    public void VisibleTabCount_VoicevoxNotInstalled_ReturnsFive()
    {
        Assert.Equal(5, SettingsTabCatalog.VisibleTabCount(voicevoxInstalled: false));
    }

    [Fact]
    public void VisibleTabCount_VoicevoxInstalled_ReturnsSix()
    {
        Assert.Equal(6, SettingsTabCatalog.VisibleTabCount(voicevoxInstalled: true));
    }
}
