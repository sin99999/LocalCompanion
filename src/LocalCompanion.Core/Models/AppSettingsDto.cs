namespace LocalCompanion.Models;

public sealed class AppSettingsDto
{
    public const double DefaultChatFontSize = 14;

    public const string DefaultChatFontFamily = "Segoe UI Variable Text";

    public bool ConfirmHistoryDelete { get; set; } = true;

    public string ThemeMode { get; set; } = AppThemeModes.Dark;

    public string ChatFontFamily { get; set; } = DefaultChatFontFamily;

    public double ChatFontSize { get; set; } = DefaultChatFontSize;

    /// <summary>チャット表示名および AI プロンプト用。空のときはローカライズされた「あなた」。</summary>
    public string UserDisplayName { get; set; } = string.Empty;

    public static AppSettingsDto CreateDefault() => new();
}

public static class AppThemeModes
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";
}
