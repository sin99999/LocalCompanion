namespace LocalCompanion.Localization;

/// <summary>UI 表示用にローカライズキーを保持する例外。</summary>
public sealed class LocalizedServiceException : Exception
{
    public LocalizedServiceException(string localizationKey, params object[] formatArgs)
        : base(localizationKey)
    {
        LocalizationKey = localizationKey;
        FormatArgs = formatArgs;
    }

    public string LocalizationKey { get; }

    public object[] FormatArgs { get; }
}
