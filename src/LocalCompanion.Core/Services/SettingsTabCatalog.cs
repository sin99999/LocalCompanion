namespace LocalCompanion.Services;

/// <summary>設定画面タブの固定構成（General / Model / Character / RAG / About + 任意 VOICEVOX）。</summary>
public static class SettingsTabCatalog
{
    public const int BaseTabCount = 5;

    public static int VisibleTabCount(bool voicevoxInstalled) =>
        BaseTabCount + (voicevoxInstalled ? 1 : 0);
}
