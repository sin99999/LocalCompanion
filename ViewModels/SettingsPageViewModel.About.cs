using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;
using Microsoft.UI.Xaml;

namespace LocalCompanion.ViewModels;

/// <summary>基本設定タブの「情報・メンテナンス」と、モデルタブの実行バックエンド表示。</summary>
public partial class SettingsPageViewModel
{
    [ObservableProperty] public partial string UiAboutSection { get; set; } = "";
    [ObservableProperty] public partial string UiAboutPrivacyNote { get; set; } = "";
    [ObservableProperty] public partial string UiAboutLicenses { get; set; } = "";
    [ObservableProperty] public partial string UiAboutOpenLogFolder { get; set; } = "";
    [ObservableProperty] public partial string UiAboutOpenTroubleshooting { get; set; } = "";
    [ObservableProperty] public partial string UiAboutBackup { get; set; } = "";
    [ObservableProperty] public partial string AppVersionText { get; set; } = "";
    [ObservableProperty] public partial string AboutStatusText { get; set; } = "";

    public Visibility AboutStatusVisibility =>
        string.IsNullOrWhiteSpace(AboutStatusText) ? Visibility.Collapsed : Visibility.Visible;

    partial void OnAboutStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(AboutStatusVisibility));

    [ObservableProperty] public partial string UiModelBackend { get; set; } = "";
    [ObservableProperty] public partial string ModelBackendText { get; set; } = "";

    private LocalizedStatusEntry? _aboutStatus;

    private void SetAboutStatus(string? localizationKey, params object[] args) =>
        SetStatus(ref _aboutStatus, v => AboutStatusText = v, localizationKey, args);

    private void ApplyLocalizedAboutUi()
    {
        UiAboutSection = _loc.Get("Settings.About.Section");
        UiAboutPrivacyNote = _loc.Get("Settings.About.PrivacyNote");
        UiAboutLicenses = _loc.Get("Settings.About.Licenses");
        UiAboutOpenLogFolder = _loc.Get("Settings.About.OpenLogFolder");
        UiAboutOpenTroubleshooting = _loc.Get("Settings.About.OpenTroubleshooting");
        UiAboutBackup = _loc.Get("Settings.About.Backup");
        UiModelBackend = _loc.Get("Settings.Model.Backend");
        AboutStatusText = ResolveStatus(_aboutStatus);
        RefreshAboutInfo();
    }

    private void RefreshAboutInfo()
    {
        AppVersionText = $"LocalCompanion {ResolveAppVersion()}";
        ModelBackendText = LlamaInstalledBackend.DescribeInstalled()
            ?? _loc.Get("Settings.Model.Backend.NotInstalled");
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(SettingsPageViewModel).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    [RelayCommand]
    private void OpenLicenses()
    {
        OpenLocalizedHelpHtml(
            "licenses",
            "THIRD-PARTY-NOTICES.txt",
            @"docs\help\licenses.html");
    }

    [RelayCommand]
    private void OpenTroubleshooting()
    {
        OpenLocalizedHelpHtml(
            "troubleshooting",
            Path.Combine("docs", "Troubleshooting.md"),
            @"docs\help\troubleshooting.html");
    }

    private void OpenLocalizedHelpHtml(string baseName, string fallbackRelativePath, string missingDisplayName)
    {
        SetAboutStatus(null);
        var lang = _loc.Current == AppLanguage.English ? "en" : "ja";
        var htmlRelativePath = Path.Combine("docs", "help", $"{baseName}.{lang}.html");
        var path = FindDistributionFile(htmlRelativePath) ?? FindDistributionFile(fallbackRelativePath);
        if (path is null)
        {
            SetAboutStatus("Settings.About.FileMissing", missingDisplayName);
            return;
        }

        OpenWithShell(path);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        SetAboutStatus(null);
        var dir = Path.GetDirectoryName(StartupLog.LogPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            SetAboutStatus("Settings.About.FileMissing", dir ?? "data");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "OpenLogFolder");
        }
    }

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        SetAboutStatus(null);
        var dataDir = Path.GetDirectoryName(StartupLog.LogPath);
        if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir))
        {
            SetAboutStatus("Settings.About.BackupFailed");
            return;
        }

        var suggested = $"LocalCompanion-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        string? dest;
        try
        {
            dest = WindowsNativeFileDialog.PickSaveFile(
                "ZIP|*.zip",
                UiAboutBackup,
                suggested,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "BackupData.Picker");
            SetAboutStatus("Settings.About.BackupFailed");
            return;
        }

        if (string.IsNullOrWhiteSpace(dest))
            return;

        try
        {
            await Task.Run(() => UserDataBackup.ExportToZip(dataDir, dest));
            SetAboutStatus("Settings.About.BackupDone", dest);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "BackupData");
            SetAboutStatus("Settings.About.BackupFailed");
        }
    }

    /// <summary>配布ルート（Root）→ ContentRoot の順で同梱ファイルを探す。</summary>
    private static string? FindDistributionFile(string relativePath)
    {
        foreach (var baseDir in new[] { AppPaths.Current.Root, AppPaths.Current.ContentRoot })
        {
            var candidate = Path.Combine(baseDir, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "OpenWithShell");
        }
    }
}
