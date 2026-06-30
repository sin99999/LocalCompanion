using System.Diagnostics;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LocalCompanion.Services;

/// <summary>起動時の LocalCompanion 本体更新確認（Release ページをブラウザで開く）。</summary>
public sealed class AppUpdateStartupCoordinator
{
    private readonly AppUpdateService _update;

    public AppUpdateStartupCoordinator(AppUpdateService update) => _update = update;

    public async Task CheckAndOfferUpdateOnStartupAsync(XamlRoot xamlRoot, CancellationToken ct = default)
    {
        AppUpdateCheckResult check;
        try
        {
            check = await _update.CheckForUpdateAsync(ct);
        }
        catch
        {
            return;
        }

        if (!_update.ShouldPrompt(check) || string.IsNullOrWhiteSpace(check.ReleasePageUrl))
            return;

        var loc = LocalizationService.Instance;
        var current = check.CurrentVersion ?? loc.Get("Common.Unknown");
        var message = loc.Format("AppUpdate.Message", check.LatestVersion!, current);

        if (!await ConfirmAsync(xamlRoot, loc.Get("AppUpdate.Title"), message))
        {
            _update.Dismiss(check);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(check.ReleasePageUrl) { UseShellExecute = true });
        }
        catch
        {
            await InfoAsync(xamlRoot, loc.Get("AppUpdate.Title"), loc.Get("AppUpdate.OpenFailed"));
        }
    }

    private static async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message)
    {
        var loc = LocalizationService.Instance;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            PrimaryButtonText = loc.Get("AppUpdate.Download"),
            SecondaryButtonText = loc.Get("Common.Later"),
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = xamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task InfoAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = LocalizationService.Instance.Get("Common.Ok"),
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }
}
