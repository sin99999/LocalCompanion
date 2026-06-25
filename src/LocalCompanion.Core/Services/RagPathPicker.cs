using LocalCompanion.Localization;

namespace LocalCompanion.Services;

public static class RagPathPicker
{
    public static bool IsSupported => WindowsNativeFileDialog.IsSupported;

    public static Task<string?> PickFileAsync(string? initialPath, nint ownerHwnd = 0) =>
        WindowsNativeFileDialog.PickOpenFileAsync(
            AppFileDialogFilters.RagIngestFilter,
            LocalizationService.Instance.Get("Settings.Rag.Picker.FileTitle"),
            initialPath,
            ownerHwnd);

    public static string? PickFile(string? initialPath, nint ownerHwnd = 0) =>
        WindowsNativeFileDialog.PickOpenFile(
            AppFileDialogFilters.RagIngestFilter,
            LocalizationService.Instance.Get("Settings.Rag.Picker.FileTitle"),
            initialPath,
            ownerHwnd);

    public static Task<string?> PickImageAttachmentFileAsync(string? initialPath = null, nint ownerHwnd = 0) =>
        WindowsNativeFileDialog.PickOpenFileAsync(
            AppFileDialogFilters.ImageAttachmentFilter,
            LocalizationService.Instance.Get("Chat.Picker.ImageTitle"),
            initialPath,
            ownerHwnd);

    public static Task<string?> PickTextAttachmentFileAsync(string? initialPath = null, nint ownerHwnd = 0) =>
        WindowsNativeFileDialog.PickOpenFileAsync(
            AppFileDialogFilters.TextAttachmentFilter,
            LocalizationService.Instance.Get("Chat.Picker.TextTitle"),
            initialPath,
            ownerHwnd);

    public static Task<string?> PickFolderAsync(string? initialPath, nint ownerHwnd = 0)
    {
        var loc = LocalizationService.Instance;
        return WindowsNativeFileDialog.PickFolderAsync(
            loc.Get("Settings.Rag.Picker.FolderDescription"),
            initialPath,
            ownerHwnd);
    }

    public static string? PickFolder(string? initialPath, nint ownerHwnd = 0)
    {
        var loc = LocalizationService.Instance;
        return WindowsNativeFileDialog.PickFolder(
            loc.Get("Settings.Rag.Picker.FolderDescription"),
            initialPath,
            ownerHwnd);
    }

    public static Task<string?> PickModelsFolderAsync(string? initialPath, nint ownerHwnd = 0)
    {
        var loc = LocalizationService.Instance;
        return WindowsNativeFileDialog.PickFolderAsync(
            loc.Get("Settings.Model.AdditionalFolder"),
            initialPath,
            ownerHwnd);
    }
}
