using LocalCompanion.Localization;

namespace LocalCompanion.Services;

public static class AppFileDialogFilters
{
    public static string ImageAttachmentFilter =>
        LocalizationService.Instance.Get("Chat.Picker.ImageFilter");

    public static string TextAttachmentFilter =>
        LocalizationService.Instance.Get("Chat.Picker.TextFilter");

    public static string RagIngestFilter =>
        RagDocumentReader.GetLocalizedFileDialogFilter();
}
