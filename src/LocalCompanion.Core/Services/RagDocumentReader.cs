using System.Text.RegularExpressions;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services.DocumentReading;

namespace LocalCompanion.Services;

public static class RagDocumentReader
{
    public const int MaxFileBytes = 20 * 1024 * 1024;

    public const string FileDialogFilter =
        "Supported files|*.txt;*.md;*.markdown;*.pdf;*.docx;*.html;*.htm;*.json;*.csv;*.xml;*.log;*.yaml;*.yml";

    internal static readonly HashSet<string> TextExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".csv", ".xml", ".html", ".htm",
        ".log", ".yaml", ".yml", ".ini", ".cfg", ".cs", ".js", ".ts", ".py", ".rtf",
        ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".rb", ".go", ".rs", ".java",
        ".kt", ".swift", ".php", ".sql", ".mdx", ".rst",
    };

    private static RagDocumentReaderRegistry? _registry;
    private static bool _usePdfLayout;

    public static string GetLocalizedFileDialogFilter() =>
        LocalizationService.Instance.Get("Settings.Rag.Picker.Filter");

    public static void Configure(bool usePdfLayoutReader)
    {
        if (_registry is not null && _usePdfLayout == usePdfLayoutReader)
            return;
        _usePdfLayout = usePdfLayoutReader;
        _registry = new RagDocumentReaderRegistry(usePdfLayoutReader);
    }

    private static RagDocumentReaderRegistry Registry =>
        _registry ??= new RagDocumentReaderRegistry(_usePdfLayout);

    public static bool IsSupported(string path) => Registry.IsSupported(path);

    public static IReadOnlyList<string> SupportedExtensionList => Registry.SupportedExtensionList;

    public static RagDocument ReadDocument(string path)
    {
        var text = ReadText(path);
        return new RagDocument(path, text);
    }

    public static RagDocument ReadDocument(Stream stream, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "upload";
        var text = ReadText(stream, fileName);
        return new RagDocument(safeName, text);
    }

    public static string ReadText(string path) => Registry.ReadText(path);

    public static string ReadText(Stream stream, string fileName) => Registry.ReadText(stream, fileName);

    public static string ExtractPlainTextFromHtml(string html)
    {
        var withoutScripts = Regex.Replace(
            html,
            @"<script\b[^>]*>.*?</script>",
            " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var withoutStyles = Regex.Replace(
            withoutScripts,
            @"<style\b[^>]*>.*?</style>",
            " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = StripHtml(withoutStyles);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @"\r\n?|\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html, "<[^>]+>", " ", RegexOptions.Singleline);
}
