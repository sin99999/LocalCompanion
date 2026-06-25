using System.Globalization;
using System.Windows.Forms;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>WinForms 共通ファイル／フォルダ選択（利用可能な拡張子のみ、*.* なし）。</summary>
public static class WindowsNativeFileDialog
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static Task<string?> PickOpenFileAsync(
        string filter,
        string? title = null,
        string? initialPath = null,
        nint ownerHwnd = 0)
    {
        if (!IsSupported)
            return Task.FromResult<string?>(null);

        return Task.Run(() => PickOpenFile(filter, title, initialPath, ownerHwnd));
    }

    public static string? PickOpenFile(
        string filter,
        string? title = null,
        string? initialPath = null,
        nint ownerHwnd = 0)
    {
        if (!IsSupported)
            return null;

        return RunSta(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = title ?? string.Empty,
                Filter = filter,
                FilterIndex = 1,
                Multiselect = false,
                CheckFileExists = true,
            };
            ApplyInitialPath(dialog, initialPath);
            return ShowOpenDialog(dialog, ownerHwnd) == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public static string? PickSaveFile(string filter, string? title = null, string? suggestedFileName = null, string? initialPath = null)
    {
        if (!IsSupported)
            return null;

        return RunSta(() =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = title ?? string.Empty,
                Filter = filter,
                FilterIndex = 1,
                OverwritePrompt = true,
                AddExtension = true,
            };
            var dir = ResolveInitialDirectory(initialPath);
            if (!string.IsNullOrWhiteSpace(dir))
                dialog.InitialDirectory = dir;
            if (!string.IsNullOrWhiteSpace(suggestedFileName))
                dialog.FileName = suggestedFileName;
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public static Task<string?> PickFolderAsync(string? description, string? initialPath, nint ownerHwnd = 0)
    {
        if (!IsSupported)
            return Task.FromResult<string?>(null);

        return Task.Run(() => PickFolder(description, initialPath, ownerHwnd));
    }

    public static string? PickFolder(string? description, string? initialPath, nint ownerHwnd = 0)
    {
        if (!IsSupported)
            return null;

        return RunSta(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description ?? string.Empty,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            var dir = ResolveInitialDirectory(initialPath);
            if (!string.IsNullOrWhiteSpace(dir))
                dialog.SelectedPath = dir;
            return ShowCommonDialog(dialog, ownerHwnd) == DialogResult.OK ? dialog.SelectedPath : null;
        });
    }

    private static DialogResult ShowOpenDialog(OpenFileDialog dialog, nint ownerHwnd) =>
        ownerHwnd != 0
            ? dialog.ShowDialog(new Win32DialogOwner(ownerHwnd))
            : dialog.ShowDialog();

    private static DialogResult ShowCommonDialog(CommonDialog dialog, nint ownerHwnd) =>
        ownerHwnd != 0
            ? dialog.ShowDialog(new Win32DialogOwner(ownerHwnd))
            : dialog.ShowDialog();

    private sealed class Win32DialogOwner(nint handle) : IWin32Window
    {
        public nint Handle { get; } = handle;
    }

    private static void ApplyInitialPath(OpenFileDialog dialog, string? initialPath)
    {
        var dir = ResolveInitialDirectory(initialPath);
        if (!string.IsNullOrWhiteSpace(dir))
            dialog.InitialDirectory = dir;
        if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            dialog.FileName = Path.GetFileName(initialPath);
    }

    private static string? ResolveInitialDirectory(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
            return null;
        if (Directory.Exists(initialPath))
            return initialPath;
        if (File.Exists(initialPath))
            return Path.GetDirectoryName(initialPath);
        var dir = Path.GetDirectoryName(initialPath);
        return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? dir : null;
    }

    internal static string? RunSta(Func<string?> action)
    {
        string? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            ApplyDialogCulture();
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(5)))
            throw new LocalizedServiceException("Settings.Rag.Error.PickerTimeout");
        if (error is not null)
            throw error;
        return result;
    }

    private static void ApplyDialogCulture()
    {
        var culture = LocalizationService.Instance.Current == AppLanguage.Japanese
            ? new CultureInfo("ja-JP")
            : new CultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
