using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using UglyToad.PdfPig;

namespace LocalCompanion.Services;

public static class RagDocumentReader
{
    public const int MaxFileBytes = 20 * 1024 * 1024;

    public const string FileDialogFilter =
        "Supported files|*.txt;*.md;*.markdown;*.pdf;*.docx;*.html;*.htm;*.json;*.csv;*.xml;*.log;*.yaml;*.yml";

    public static string GetLocalizedFileDialogFilter() =>
        LocalizationService.Instance.Get("Settings.Rag.Picker.Filter");

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".csv", ".xml", ".html", ".htm",
        ".log", ".yaml", ".yml", ".ini", ".cfg", ".cs", ".js", ".ts", ".py", ".rtf",
    };

    private static readonly HashSet<string> SupportedExtensions = new(TextExtensions, StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx",
    };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public static IReadOnlyList<string> SupportedExtensionList =>
        SupportedExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

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

    public static string ReadText(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new LocalizedServiceException("Settings.Rag.Error.FileNotFound");
        if (info.Length > MaxFileBytes)
            throw new LocalizedServiceException(
                "Settings.Rag.Error.FileTooLargeNamed",
                MaxFileBytes / (1024 * 1024),
                path);

        var ext = Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".pdf" => ReadPdf(path),
            ".docx" => ReadDocx(path),
            ".html" or ".htm" => StripHtml(ReadUtf8(path)),
            _ when TextExtensions.Contains(ext) => ReadUtf8(path),
            _ => throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", ext),
        };
    }

    public static string ReadText(Stream stream, string fileName)
    {
        if (stream.CanSeek && stream.Length > MaxFileBytes)
            throw new LocalizedServiceException(
                "Settings.Rag.Error.FileTooLargeNamed",
                MaxFileBytes / (1024 * 1024),
                fileName);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ReadPdf(stream),
            ".docx" => ReadDocx(stream),
            ".html" or ".htm" => StripHtml(ReadUtf8(stream)),
            _ when TextExtensions.Contains(ext) => ReadUtf8(stream),
            _ => throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", ext),
        };
    }

    private static string ReadUtf8(string path) =>
        File.ReadAllText(path, DetectEncoding(path));

    private static string ReadUtf8(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length > MaxFileBytes)
            throw new LocalizedServiceException("Settings.Rag.Error.FileTooLarge", MaxFileBytes / (1024 * 1024));
        return DetectEncoding(bytes).GetString(bytes);
    }

    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return DetectEncoding(bytes);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        return Encoding.UTF8;
    }

    private static string ReadPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        return ExtractPdfText(doc);
    }

    private static string ReadPdf(Stream stream)
    {
        using var doc = PdfDocument.Open(stream);
        return ExtractPdfText(doc);
    }

    private static string ExtractPdfText(PdfDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine($"--- ページ {page.Number} ---");
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string ReadDocx(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadDocx(stream);
    }

    private static string ReadDocx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new LocalizedServiceException("Settings.Rag.Error.WordBodyMissing");
        using var xmlStream = entry.Open();
        var doc = XDocument.Load(xmlStream);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var parts = doc.Descendants(ns + "t").Select(x => x.Value).Where(v => v.Length > 0);
        return string.Join("\n", parts);
    }

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
