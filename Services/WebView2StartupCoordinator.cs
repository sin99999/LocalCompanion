using System.Diagnostics;
using LocalCompanion.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LocalCompanion.Services;

/// <summary>WebView2 Runtime 未導入時に公式ダウンロード案内を出す。</summary>
public sealed class WebView2StartupCoordinator
{
    public async Task OfferInstallIfMissingAsync(XamlRoot xamlRoot)
    {
        if (WebView2RuntimeAvailability.IsInstalled())
            return;

        var loc = LocalizationService.Instance;
        var dialog = new ContentDialog
        {
            Title = loc.Get("WebView2.Missing.Title"),
            Content = new TextBlock
            {
                Text = loc.Get("WebView2.Missing.Message"),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = loc.Get("WebView2.Missing.OpenDownload"),
            CloseButtonText = loc.Get("Common.Later"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(WebView2RuntimeAvailability.DownloadUrl)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            var fail = new ContentDialog
            {
                Title = loc.Get("WebView2.Missing.Title"),
                Content = new TextBlock
                {
                    Text = loc.Get("WebView2.Missing.OpenFailed"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                CloseButtonText = loc.Get("Common.Ok"),
                XamlRoot = xamlRoot,
            };
            await fail.ShowAsync();
        }
    }
}
