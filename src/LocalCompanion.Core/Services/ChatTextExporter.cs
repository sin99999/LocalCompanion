using System.Text;
using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット応答をデスクトップ等へテキストファイルとして書き出す。</summary>
public static class ChatTextExporter
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFileNameStemChars = 40;

    public static async Task<string> AppendExportNoticeAsync(
        LlamaServerClient llama,
        string chatReply,
        ChatExportRequest export,
        string[]? ragSources,
        bool japanese,
        CancellationToken ct)
    {
        var loc = LocalizationService.Instance;
        var document = await ChatExportDocumentFormatter.FormatAsync(
            llama, export, chatReply, ragSources, japanese, ct);

        var path = TryExport(export, document, ragSources, japanese, out var error);
        if (path is not null)
            return chatReply + "\n\n" + loc.Format("Chat.Export.Saved", path);

        var reason = string.IsNullOrWhiteSpace(error)
            ? loc.Get("Chat.Export.Error.Unknown")
            : error;
        return chatReply + "\n\n" + loc.Format("Chat.Export.Failed", reason);
    }

    public static string? TryExport(
        ChatExportRequest request,
        ChatExportDocument document,
        string[]? ragSources,
        bool japanese,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(document.Body))
        {
            errorMessage = LocalizationService.Instance.Get("Chat.Export.Error.EmptyBody");
            return null;
        }

        var directory = ResolveDirectory(request.Destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            errorMessage = LocalizationService.Instance.Get("Chat.Export.Error.Destination");
            return null;
        }

        Directory.CreateDirectory(directory);
        var fileName = BuildFileName(request, document.Title);
        var path = ResolveUniquePath(Path.Combine(directory, fileName));
        var content = BuildDocument(document.Body.Trim(), request, document.Title, ragSources, japanese);

        if (Encoding.UTF8.GetByteCount(content) > MaxFileBytes)
        {
            errorMessage = LocalizationService.Instance.Get("Chat.Export.Error.TooLarge");
            return null;
        }

        try
        {
            AtomicFile.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return null;
        }
    }

    internal static string BuildDocument(
        string body,
        ChatExportRequest request,
        string title,
        string[]? ragSources,
        bool japanese)
    {
        var ext = ChatTextExportFormats.NormalizeExtension(request.Extension);
        if (ext is ".json")
            return BuildJsonDocument(body, title);

        if (ext is ".csv" && LooksLikeMarkdownTable(body))
            return ConvertMarkdownTableToCsv(body);

        if (ext is ".md" or ".markdown" or ".mdx" or ".rst")
            return BuildMarkdownDocument(body, title, ragSources, japanese);

        if (ext is ".html" or ".htm")
            return BuildHtmlDocument(body, title, ragSources, japanese);

        if (ext is ".xml")
            return BuildXmlDocument(body, title);

        return BuildPlainDocument(body, title, ragSources, japanese);
    }

    private static string BuildFileName(ChatExportRequest request, string titleStem)
    {
        var stem = SanitizeFileStem(request.FileNameStem);
        if (string.IsNullOrWhiteSpace(stem))
            stem = SanitizeFileStem(titleStem);
        if (string.IsNullOrWhiteSpace(stem))
            stem = "LocalCompanion-export";

        if (stem.Length > MaxFileNameStemChars)
            stem = stem[..MaxFileNameStemChars].TrimEnd('_', '.');

        return stem + ChatTextExportFormats.NormalizeExtension(request.Extension);
    }

    private static string SanitizeFileStem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var stem = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');

        stem = stem.Replace(' ', '_');
        return stem.Trim('_', '.', '。', '、', '！', '？', '!', '?');
    }

    private static string? ResolveDirectory(ChatExportDestination destination) =>
        destination switch
        {
            ChatExportDestination.Desktop =>
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            _ => null,
        };

    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static string BuildPlainDocument(
        string body,
        string title,
        string[]? ragSources,
        bool japanese)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Min(title.Length, 60)));
        sb.AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz"));
        sb.AppendLine();
        sb.AppendLine(body);
        AppendSources(sb, ragSources, japanese);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildMarkdownDocument(
        string body,
        string title,
        string[]? ragSources,
        bool japanese)
    {
        var sb = new StringBuilder();
        if (!body.TrimStart().StartsWith('#'))
        {
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.AppendLine($"*{DateTimeOffset.Now:yyyy-MM-dd HH:mm}*");
            sb.AppendLine();
        }

        sb.AppendLine(body);
        if (ragSources is { Length: > 0 } && !body.Contains("## 出典", StringComparison.Ordinal) && !body.Contains("## Sources", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine(japanese ? "## 出典" : "## Sources");
            foreach (var source in ragSources.Distinct(StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- {source}");
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildHtmlDocument(
        string body,
        string title,
        string[]? ragSources,
        bool japanese)
    {
        if (body.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            var wrapped = new StringBuilder();
            wrapped.AppendLine("<!DOCTYPE html>");
            wrapped.AppendLine("<html lang=\"ja\">");
            wrapped.AppendLine("<head>");
            wrapped.AppendLine("  <meta charset=\"utf-8\">");
            wrapped.AppendLine($"  <title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
            wrapped.AppendLine("</head>");
            wrapped.AppendLine("<body>");
            wrapped.AppendLine(body);
            if (ragSources is { Length: > 0 })
            {
                wrapped.AppendLine(japanese ? "  <h2>出典</h2>" : "  <h2>Sources</h2>");
                wrapped.AppendLine("  <ul>");
                foreach (var source in ragSources.Distinct(StringComparer.OrdinalIgnoreCase))
                    wrapped.AppendLine($"    <li>{System.Net.WebUtility.HtmlEncode(source)}</li>");
                wrapped.AppendLine("  </ul>");
            }

            wrapped.AppendLine("</body>");
            wrapped.AppendLine("</html>");
            return wrapped.ToString();
        }

        var escapedTitle = System.Net.WebUtility.HtmlEncode(title);
        var escapedBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>\n", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ja\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine($"  <title>{escapedTitle}</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"  <h1>{escapedTitle}</h1>");
        sb.AppendLine($"  <p><em>{DateTimeOffset.Now:yyyy-MM-dd HH:mm}</em></p>");
        sb.AppendLine($"  <div>{escapedBody}</div>");
        if (ragSources is { Length: > 0 })
        {
            sb.AppendLine(japanese ? "  <h2>出典</h2>" : "  <h2>Sources</h2>");
            sb.AppendLine("  <ul>");
            foreach (var source in ragSources.Distinct(StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"    <li>{System.Net.WebUtility.HtmlEncode(source)}</li>");
            sb.AppendLine("  </ul>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string BuildJsonDocument(string body, string title)
    {
        if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('['))
            return body.TrimEnd() + Environment.NewLine;

        var payload = new
        {
            title,
            generatedAt = DateTimeOffset.Now,
            content = body,
        };
        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + Environment.NewLine;
    }

    private static string BuildXmlDocument(string body, string title)
    {
        if (body.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            return body.TrimEnd() + Environment.NewLine;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<document>");
        sb.AppendLine($"  <title>{System.Security.SecurityElement.Escape(title)}</title>");
        sb.AppendLine($"  <generatedAt>{DateTimeOffset.Now:O}</generatedAt>");
        sb.AppendLine("  <content><![CDATA[");
        sb.AppendLine(body);
        sb.AppendLine("  ]]></content>");
        sb.AppendLine("</document>");
        return sb.ToString();
    }

    private static void AppendSources(StringBuilder sb, string[]? ragSources, bool japanese)
    {
        if (ragSources is not { Length: > 0 })
            return;

        sb.AppendLine();
        sb.AppendLine(japanese ? "出典:" : "Sources:");
        foreach (var source in ragSources.Distinct(StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- {source}");
    }

    private static bool LooksLikeMarkdownTable(string body)
    {
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length >= 2
               && lines[0].Contains('|', StringComparison.Ordinal)
               && lines[1].Contains('-', StringComparison.Ordinal)
               && lines[1].Contains('|', StringComparison.Ordinal);
    }

    private static string ConvertMarkdownTableToCsv(string body)
    {
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<string>();
        foreach (var line in lines)
        {
            if (!line.Contains('|', StringComparison.Ordinal))
                continue;
            if (line.Contains("---", StringComparison.Ordinal))
                continue;

            var cells = line.Trim('|').Split('|', StringSplitOptions.TrimEntries);
            rows.Add(string.Join(",", cells.Select(EscapeCsvCell)));
        }

        return rows.Count > 0
            ? string.Join(Environment.NewLine, rows) + Environment.NewLine
            : body + Environment.NewLine;
    }

    private static string EscapeCsvCell(string cell)
    {
        if (cell.Contains('"', StringComparison.Ordinal) || cell.Contains(',', StringComparison.Ordinal))
            return "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return cell;
    }
}
