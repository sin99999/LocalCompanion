using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LocalCompanion.Services;

/// <summary>起動時の VOICEVOX 更新確認ダイアログと、更新後の話者反映。</summary>
public sealed class VoicevoxStartupCoordinator
{
    private readonly VoicevoxUpdateService _update;
    private readonly VoicevoxClient _client;
    private readonly VoicevoxLifecycleService _lifecycle;
    private readonly VoicevoxInstallLocator _locator;
    private readonly VoicevoxSpeakerCacheStore _speakerCache;
    private readonly Microsoft.Extensions.Options.IOptions<VoicevoxOptions> _opt;

    public VoicevoxStartupCoordinator(
        VoicevoxUpdateService update,
        VoicevoxClient client,
        VoicevoxLifecycleService lifecycle,
        VoicevoxInstallLocator locator,
        VoicevoxSpeakerCacheStore speakerCache,
        Microsoft.Extensions.Options.IOptions<VoicevoxOptions> opt)
    {
        _update = update;
        _client = client;
        _lifecycle = lifecycle;
        _locator = locator;
        _speakerCache = speakerCache;
        _opt = opt;
    }

    public async Task CheckAndOfferUpdateOnStartupAsync(XamlRoot xamlRoot, CancellationToken ct = default)
    {
        if (!_opt.Value.UpdateCheckOnStartup || !_locator.IsInstalled)
            return;

        VoicevoxUpdateCheckResult check;
        try
        {
            check = await _update.CheckForUpdateAsync(ct);
        }
        catch
        {
            return;
        }

        if (!check.UpdateAvailable || string.IsNullOrWhiteSpace(check.LatestVersion))
            return;

        var loc = LocalizationService.Instance;
        var current = check.CurrentVersion ?? loc.Get("Common.Unknown");
        var message = loc.Format("Voicevox.Update.Message", check.LatestVersion, current);

        if (!await ConfirmAsync(xamlRoot, loc.Get("Voicevox.Update.Title"), message))
            return;

        var progress = new ContentDialog
        {
            Title = loc.Get("Voicevox.Update.Title"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new ProgressRing
                    {
                        IsActive = true,
                        Width = 48,
                        Height = 48,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = loc.Get("Voicevox.Update.Progress"),
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                },
            },
            XamlRoot = xamlRoot,
        };
        var showTask = progress.ShowAsync();

        var ok = false;
        try
        {
            ok = await _update.ApplyUpdateAsync(check, ct);
        }
        finally
        {
            try { progress.Hide(); } catch { /* ignore */ }
            try { await showTask; } catch { /* ignore */ }
        }

        if (!ok)
        {
            await InfoAsync(xamlRoot, loc.Get("Voicevox.Update.Title"), loc.Get("Voicevox.Update.Failed"));
            return;
        }

        await RefreshSpeakersAfterUpdateAsync(xamlRoot, ct);
    }

    public async Task RefreshSpeakersAfterUpdateAsync(XamlRoot? xamlRoot, CancellationToken ct = default)
    {
        var loc = LocalizationService.Instance;
        if (!_locator.IsInstalled)
            return;

        var status = await _lifecycle.EnsureRunningAsync(ct);
        if (!status.Available)
            return;

        IReadOnlyList<VoicevoxSpeakerStyleDto> speakers;
        try
        {
            speakers = await _client.ListSpeakersAsync(ct);
        }
        catch
        {
            return;
        }

        var added = _speakerCache.FindNewSpeakers(speakers);
        _speakerCache.Save(speakers);

        try
        {
            var settingsVm = AppServices.Get<SettingsPageViewModel>();
            await settingsVm.LoadVoicevoxSpeakersAsync(ct);
        }
        catch
        {
            /* 設定画面未生成時はキャッシュ更新のみ */
        }

        if (added.Count > 0 && xamlRoot is not null)
        {
            var names = string.Join(
                loc.Get("Voicevox.Speaker.ListSeparator"),
                added.Take(5).Select(s => VoicevoxSpeakerLocalizer.FormatDisplayName(s.Id, s.SpeakerName, s.StyleName)));
            if (added.Count > 5)
                names += loc.Format("Voicevox.Speakers.More", added.Count - 5);
            await InfoAsync(
                xamlRoot,
                loc.Get("Voicevox.Speakers.Updated.Title"),
                loc.Format("Voicevox.Speakers.Updated.Message", added.Count, names));
        }
    }

    private static async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message)
    {
        var loc = LocalizationService.Instance;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            PrimaryButtonText = loc.Get("Common.Yes"),
            SecondaryButtonText = loc.Get("Common.No"),
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
