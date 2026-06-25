using LocalCompanion.Models;
using Microsoft.UI.Xaml;

namespace LocalCompanion.Services;

public sealed class AppAppearanceService
{
    private readonly AppSettingsStore _store;

    public AppAppearanceService(AppSettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public AppSettingsDto Current { get; private set; }

    public event EventHandler? Changed;

    public void ReloadFromStore()
    {
        Current = _store.Load();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public AppSettingsDto Save(AppSettingsDto dto)
    {
        Current = _store.Save(dto);
        ApplyRuntimeTheme();
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public bool ShouldConfirmHistoryDelete() => Current.ConfirmHistoryDelete;

    public void SetConfirmHistoryDelete(bool enabled)
    {
        Current = _store.Save(new AppSettingsDto
        {
            ConfirmHistoryDelete = enabled,
            ThemeMode = Current.ThemeMode,
            ChatFontFamily = Current.ChatFontFamily,
            ChatFontSize = Current.ChatFontSize,
            UserDisplayName = Current.UserDisplayName,
        });
    }

    public static ApplicationTheme ResolveApplicationTheme(string _) => ApplicationTheme.Dark;

    public void ApplyRuntimeTheme()
    {
        try
        {
            Application.Current.RequestedTheme = ApplicationTheme.Dark;
        }
        catch
        {
            /* 実行中の Application.RequestedTheme 変更が拒否される環境向け */
        }

        if (MainWindow.Instance?.Content is FrameworkElement root)
            root.RequestedTheme = ElementTheme.Dark;
    }
}
