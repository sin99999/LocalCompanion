using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LocalCompanion.Services;

/// <summary>WinUI 標準のファイル選択（アプリ言語・親ウィンドウに連動）。</summary>
public static class WinUiStoragePickerService
{
    private static readonly string[] TextExtensions = [".txt", ".md", ".json", ".csv", ".log", ".xml", ".yaml", ".yml"];

    public static Task<StorageFile?> PickImageFileAsync()
    {
        var picker = CreateOpenPicker(PickerViewMode.Thumbnail, PickerLocationId.PicturesLibrary);
        foreach (var ext in ChatAttachmentFileRules.ImageExtensionList)
            picker.FileTypeFilter.Add(ext);
        return picker.PickSingleFileAsync().AsTask();
    }

    public static Task<StorageFile?> PickTextFileAsync()
    {
        var picker = CreateOpenPicker(PickerViewMode.List, PickerLocationId.DocumentsLibrary);
        foreach (var ext in TextExtensions)
            picker.FileTypeFilter.Add(ext);
        return picker.PickSingleFileAsync().AsTask();
    }

    private static FileOpenPicker CreateOpenPicker(PickerViewMode viewMode, PickerLocationId start)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = viewMode,
            SuggestedStartLocation = start,
        };
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return picker;
    }
}
