namespace LocalCompanion.Services;

public static class ChatAttachmentFileRules
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
    };

    public static IReadOnlyList<string> ImageExtensionList { get; } =
        ImageExtensions.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsSupportedImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return ImageExtensions.Contains(Path.GetExtension(path));
    }
}
