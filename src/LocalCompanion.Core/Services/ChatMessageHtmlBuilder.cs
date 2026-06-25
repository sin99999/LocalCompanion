using System.Net;
using System.Text;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット用 Markdown（表を含む）を表示用 HTML に変換する。</summary>
public static class ChatMessageHtmlBuilder
{
    public static string BuildDocument(string? sourceText, bool sentenceBreaks = true, bool secondary = false)
    {
        var body = BuildBody(sourceText, sentenceBreaks);
        var bodyClass = secondary ? "secondary" : string.Empty;
        return $$"""
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<style>
html, body {
  margin: 0;
  padding: 0;
  background: transparent;
  color: #f3f3f3;
  font-family: "Segoe UI", "Yu Gothic UI", sans-serif;
  font-size: 14px;
  line-height: 1.55;
  -webkit-user-select: text;
  user-select: text;
  word-wrap: break-word;
  overflow-wrap: anywhere;
}
body.secondary {
  color: rgba(243, 243, 243, 0.78);
  font-size: 12px;
}
p { margin: 0.45em 0; }
ul, ol { margin: 0.45em 0; padding-left: 1.35em; }
li { margin: 0.2em 0; }
table.chat-table {
  width: 100%;
  max-width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  margin: 0.6em 0;
}
table.chat-table th,
table.chat-table td {
  border: 1px solid rgba(255, 255, 255, 0.18);
  padding: 6px 8px;
  vertical-align: top;
  word-wrap: break-word;
  overflow-wrap: anywhere;
}
table.chat-table th {
  background: rgba(255, 255, 255, 0.08);
  font-weight: 600;
  font-size: 13px;
}
table.chat-table td { font-size: 13px; }
pre.chat-code {
  margin: 0.6em 0;
  padding: 10px 12px;
  background: rgba(0, 0, 0, 0.28);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
  font-size: 13px;
  line-height: 1.45;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
</head>
<body class="{{bodyClass}}">
{{body}}
</body>
</html>
""";
    }

    public static string BuildBody(string? sourceText, bool sentenceBreaks = true)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return string.Empty;

        var blocks = ChatRichContentParser.ParseBlocks(sourceText, sentenceBreaks);
        if (blocks.Count == 0)
            return $"<p>{Esc(sourceText)}</p>";

        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case ChatDisplayBlockKind.Paragraph:
                    foreach (var line in block.ParagraphLines)
                    {
                        if (line.Length > 0)
                            sb.Append("<p>").Append(Esc(Sanitize(line))).Append("</p>");
                    }
                    break;
                case ChatDisplayBlockKind.List:
                    sb.Append(block.ListOrdered ? "<ol>" : "<ul>");
                    foreach (var item in block.ListItems)
                        sb.Append("<li>").Append(Esc(Sanitize(item))).Append("</li>");
                    sb.Append(block.ListOrdered ? "</ol>" : "</ul>");
                    break;
                case ChatDisplayBlockKind.Table:
                    sb.Append(BuildTable(block.TableHeader, block.TableRows));
                    break;
                case ChatDisplayBlockKind.Code:
                    sb.Append("<pre class=\"chat-code\">")
                        .Append(Esc(block.CodeText))
                        .Append("</pre>");
                    break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : $"<p>{Esc(sourceText)}</p>";
    }

    private static string BuildTable(
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (header.Count == 0)
            return string.Empty;

        var columnCount = Math.Max(header.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        if (columnCount == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<table class=\"chat-table\"><thead><tr>");
        for (var c = 0; c < columnCount; c++)
        {
            var cell = c < header.Count ? Sanitize(header[c]) : string.Empty;
            sb.Append("<th>").Append(Esc(cell)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            for (var c = 0; c < columnCount; c++)
            {
                var cell = c < row.Count ? Sanitize(row[c]) : string.Empty;
                sb.Append("<td>").Append(Esc(cell)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string Sanitize(string? text) => ChatTableBoxFormatter.SanitizeCell(text);

    private static string Esc(string text) => WebUtility.HtmlEncode(text);
}
