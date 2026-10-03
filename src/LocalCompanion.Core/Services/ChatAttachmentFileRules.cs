namespace LocalCompanion.Services;

public static class ChatAttachmentFileRules
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
    };

    public static IReadOnlyList<string> ImageExtensionList { get; } =
        ImageExtensions.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsSupportedTextPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var ext = Path.GetExtension(path);
        return RagDocumentReader.TextExtensionSet.Contains(ext);
    }

    public static bool IsSupportedImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsWebp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12
        && bytes[0] == (byte)'R'
        && bytes[1] == (byte)'I'
        && bytes[2] == (byte)'F'
        && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W'
        && bytes[9] == (byte)'E'
        && bytes[10] == (byte)'B'
        && bytes[11] == (byte)'P';

    public static string ToDataUrl(ReadOnlySpan<byte> bytes) =>
        $"data:{MimeOf(bytes)};base64,{Convert.ToBase64String(bytes)}";

    public static string ToDataUrl(string base64OrDataUrl)
    {
        var data = base64OrDataUrl.Trim();
        if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return data;

        var bytes = Convert.FromBase64String(data);
        return $"data:{MimeOf(bytes)};base64,{data}";
    }

    internal static string MimeOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return "image/gif";
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            return "image/bmp";
        if (IsWebp(bytes))
            return "image/webp";
        return "image/jpeg";
    }
}
