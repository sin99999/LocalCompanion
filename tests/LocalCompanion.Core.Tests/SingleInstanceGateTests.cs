using LocalCompanion;
using LocalCompanion.Localization;

namespace LocalCompanion.Core.Tests;

public sealed class SingleInstanceGateTests
{
    [Fact]
    public void AlreadyRunningResources_ExistInJapaneseAndEnglish()
    {
        var ja = LocalizationResources.For(AppLanguage.Japanese);
        var en = LocalizationResources.For(AppLanguage.English);

        Assert.Contains("すでに起動", ja["App.AlreadyRunning"]);
        Assert.Contains("ウィンドウ", ja["App.AlreadyRunning"]);
        Assert.Equal("LocalCompanion", ja["App.AlreadyRunning.Title"]);

        Assert.Contains("already running", en["App.AlreadyRunning"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window", en["App.AlreadyRunning"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("LocalCompanion", en["App.AlreadyRunning.Title"]);
    }

    [Fact]
    public void GetAlreadyRunningMessage_UsesSavedLanguageTable()
    {
        Assert.Contains("すでに起動", SingleInstanceGate.GetAlreadyRunningMessage(AppLanguage.Japanese));
        Assert.Contains("already running", SingleInstanceGate.GetAlreadyRunningMessage(AppLanguage.English), StringComparison.OrdinalIgnoreCase);
    }
}
