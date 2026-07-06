namespace LocalCompanion.Services;

/// <summary>チャット結果のデスクトップ書き出しで許可するテキスト系拡張子（ソースコード除く）。</summary>
public static class ChatTextExportFormats
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".txt", ".md", ".markdown", ".mdx", ".rst",
        ".csv", ".json", ".xml", ".html", ".htm",
        ".yaml", ".yml", ".log", ".ini", ".cfg",
    ];

    private static readonly HashSet<string> ExtensionSet =
        new(Extensions, StringComparer.OrdinalIgnoreCase);

    public const string DefaultExtension = ".md";

    public static bool IsAllowed(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ExtensionSet.Contains(ext);
    }

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return DefaultExtension;

        var ext = extension.Trim();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        return IsAllowed(ext) ? ext.ToLowerInvariant() : DefaultExtension;
    }
}
