namespace LocalCompanion;

public sealed class AppUpdateOptions
{
    public const string SectionName = "AppUpdate";

    /// <summary>起動完了後に GitHub Release を確認する。</summary>
    public bool UpdateCheckOnStartup { get; set; } = true;

    /// <summary>GitHub リポジトリ（owner/name）。</summary>
    public string GitHubRepo { get; set; } = "sin99999/LocalCompanion";

    /// <summary>更新確認の最短間隔（時間）。</summary>
    public int CheckIntervalHours { get; set; } = 24;
}
