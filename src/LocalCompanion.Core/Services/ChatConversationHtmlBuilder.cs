using System.Net;
using System.Text;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 会話ログを WebView2 向け HTML にする（DOM 選択で発言またぎコピー可能）。
/// Cursor / VS Code チャットと同様、選択エンジンはブラウザ側。
/// </summary>
public static class ChatConversationHtmlBuilder
{
    public readonly record struct Line(
        string Header,
        string? ReasoningText,
        string BodyText,
        bool ApplySentenceBreaks,
        string? ReasoningLabel = null,
        bool LiveStream = false,
        bool ShowReasoningPanel = false);

    public static string BuildShell(
        string fontFamily,
        double fontSize,
        string? copyLabel = null,
        string? copiedLabel = null)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        var size = fontSize > 0 ? fontSize : 14;
        var familyCss = WebUtility.HtmlEncode(family);
        var copy = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(copyLabel) ? "コピー" : copyLabel.Trim());
        var copied = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(copiedLabel) ? "コピーした" : copiedLabel.Trim());
        return $$"""
<!DOCTYPE html>
<html lang="ja" data-copy-label="{{copy}}" data-copied-label="{{copied}}">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<style>
:root {
  --chat-fg: #f3f3f3;
  --chat-fg-secondary: rgba(243, 243, 243, 0.78);
  --chat-link: #60cdff;
  --chat-font: "{{familyCss}}", "Yu Gothic UI", sans-serif;
  --chat-size: {{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
}
html, body {
  margin: 0;
  padding: 8px 4px 16px 0;
  background: transparent;
  color: var(--chat-fg);
  font-family: var(--chat-font);
  font-size: var(--chat-size);
  line-height: 1.55;
  -webkit-user-select: text;
  user-select: text;
  word-wrap: break-word;
  overflow-wrap: anywhere;
}
#log { min-height: 100%; }
.msg { margin: 0 0 14px 0; }
.msg .header {
  font-weight: 600;
  margin: 0 0 4px 0;
  user-select: text;
}
.msg .reasoning {
  color: var(--chat-fg-secondary);
  font-size: 0.92em;
  margin: 0 0 10px 0;
  padding: 8px 10px;
  border-left: 3px solid rgba(96, 205, 255, 0.55);
  background: rgba(255, 255, 255, 0.06);
  border-radius: 0 6px 6px 0;
}
.msg .reasoning.live {
  border-left-color: rgba(96, 205, 255, 0.95);
  background: rgba(96, 205, 255, 0.08);
}
.msg .reasoning-label {
  font-weight: 600;
  font-size: 0.85em;
  letter-spacing: 0.02em;
  margin: 0 0 6px 0;
  color: var(--chat-link);
  user-select: text;
}
.msg .reasoning-body {
  white-space: pre-wrap;
  margin: 0;
}
.msg .stream-caret {
  display: inline-block;
  width: 0.55em;
  margin-left: 1px;
  animation: lcCaret 1s step-end infinite;
  color: var(--chat-link);
}
@keyframes lcCaret {
  50% { opacity: 0; }
}
.msg a {
  color: var(--chat-link);
  cursor: pointer;
}
p { margin: 0.45em 0; }
.msg {
  background: rgba(255, 255, 255, 0.02);
}
ul.chat-list, ol.chat-list {
  list-style: none;
  margin: 0.45em 0;
  padding-left: 0;
}
.chat-list li {
  margin: 0.2em 0;
  -webkit-user-select: text;
  user-select: text;
}
.list-mark {
  -webkit-user-select: text;
  user-select: text;
}
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
  font-size: 0.93em;
}
.chat-code-wrap {
  position: relative;
  margin: 0.6em 0;
}
pre.chat-code {
  margin: 0.6em 0;
  padding: 10px 12px;
  background: rgba(0, 0, 0, 0.28);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
  font-size: 0.93em;
  line-height: 1.45;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.chat-code-wrap > pre.chat-code {
  margin: 0;
  padding-right: 5.4em;
}
button.chat-copy {
  position: absolute;
  top: 6px;
  right: 6px;
  z-index: 1;
  border: 1px solid rgba(255, 255, 255, 0.22);
  background: rgba(0, 0, 0, 0.45);
  color: var(--chat-fg);
  border-radius: 4px;
  padding: 2px 8px;
  font-family: var(--chat-font);
  font-size: 0.78em;
  line-height: 1.3;
  cursor: pointer;
  -webkit-user-select: none;
  user-select: none;
}
button.chat-copy:hover {
  background: rgba(96, 205, 255, 0.22);
}
button.chat-copy.copied {
  border-color: rgba(96, 205, 255, 0.55);
}
code {
  font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
  font-size: 0.93em;
  background: rgba(0, 0, 0, 0.28);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 4px;
  padding: 0.1em 0.35em;
}
strong { font-weight: 700; }
</style>
</head>
<body>
<div id="log"></div>
<script>
function lcScrollEnd() {
  window.scrollTo(0, document.body.scrollHeight);
}
function lcIsNearEnd() {
  var gap = document.documentElement.scrollHeight - (window.scrollY + window.innerHeight);
  return gap < 48;
}
function lcNotifyScroll() {
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ type: 'scroll', atEnd: lcIsNearEnd() });
    }
  } catch (e) { /* ignore */ }
}
function lcCopyLabels() {
  var el = document.documentElement;
  return {
    copy: el.getAttribute('data-copy-label') || 'コピー',
    copied: el.getAttribute('data-copied-label') || 'コピーした'
  };
}
function lcFillCopyButtons() {
  var label = lcCopyLabels().copy;
  var buttons = document.querySelectorAll('button.chat-copy');
  for (var i = 0; i < buttons.length; i++) {
    if (!buttons[i].classList.contains('copied'))
      buttons[i].textContent = label;
  }
}
function lcCopyBlock(btn) {
  var wrap = btn.closest('.chat-code-wrap');
  var pre = wrap && wrap.querySelector('pre.chat-code');
  var text = pre ? (pre.innerText || '') : '';
  if (!text) return;
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ type: 'copy', text: text });
    }
  } catch (e) { /* ignore */ }
  var labels = lcCopyLabels();
  btn.textContent = labels.copied;
  btn.classList.add('copied');
  setTimeout(function () {
    btn.textContent = labels.copy;
    btn.classList.remove('copied');
  }, 1400);
}
function lcHasTextSelection() {
  var sel = window.getSelection();
  return !!(sel && sel.rangeCount > 0 && !sel.isCollapsed);
}
function lcApplyLog(html, scroll) {
  document.getElementById('log').innerHTML = html || '';
  lcFillCopyButtons();
  if (scroll) lcScrollEnd();
}
function lcSetLog(html, scroll) {
  if (lcHasTextSelection()) {
    window.__lcPendingLog = html || '';
    window.__lcPendingScroll = !!scroll;
    window.__lcPendingPatch = null;
    return 'deferred';
  }
  window.__lcPendingLog = null;
  window.__lcPendingPatch = null;
  lcApplyLog(html, !!scroll);
  return 'ok';
}
function lcPatchLastArticle(html, scroll) {
  if (lcHasTextSelection()) {
    window.__lcPendingPatch = html || '';
    window.__lcPendingScroll = !!scroll;
    return 'deferred';
  }
  window.__lcPendingPatch = null;
  var log = document.getElementById('log');
  var articles = log.querySelectorAll('article.msg');
  if (articles.length === 0) {
    log.insertAdjacentHTML('beforeend', html || '');
  } else {
    articles[articles.length - 1].outerHTML = html || '';
  }
  if (scroll) lcScrollEnd();
  lcFillCopyButtons();
  return 'ok';
}
function lcSetAppearance(family, size) {
  document.documentElement.style.setProperty('--chat-font', '"' + family + '", "Yu Gothic UI", sans-serif');
  document.documentElement.style.setProperty('--chat-size', size + 'px');
}
document.addEventListener('selectionchange', function () {
  if (lcHasTextSelection()) return;
  if (window.__lcPendingLog != null) {
    var pending = window.__lcPendingLog;
    var scroll = !!window.__lcPendingScroll;
    window.__lcPendingLog = null;
    window.__lcPendingPatch = null;
    lcApplyLog(pending, scroll);
    return;
  }
  if (window.__lcPendingPatch != null) {
    var patch = window.__lcPendingPatch;
    var pscroll = !!window.__lcPendingScroll;
    window.__lcPendingPatch = null;
    lcPatchLastArticle(patch, pscroll);
  }
});
window.addEventListener('scroll', lcNotifyScroll, { passive: true });
document.getElementById('log').addEventListener('click', function (ev) {
  var btn = ev.target && ev.target.closest ? ev.target.closest('button.chat-copy') : null;
  if (!btn) return;
  ev.preventDefault();
  lcCopyBlock(btn);
});
</script>
</body>
</html>
""";
    }

    public static string BuildLogHtml(IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
            sb.Append(BuildArticleHtml(lines[i]));
        return sb.ToString();
    }

    public static string BuildArticleHtml(Line line)
    {
        var sb = new StringBuilder();
        sb.Append("<article class=\"msg\">");
        if (!string.IsNullOrWhiteSpace(line.Header))
            sb.Append("<div class=\"header\">").Append(Esc(line.Header)).Append("</div>");

        var showReasoning = line.ShowReasoningPanel;
        if (showReasoning)
        {
            sb.Append("<div class=\"")
                .Append(line.LiveStream ? "reasoning live" : "reasoning")
                .Append("\">");
            if (!string.IsNullOrWhiteSpace(line.ReasoningLabel))
            {
                sb.Append("<div class=\"reasoning-label\">")
                    .Append(Esc(line.ReasoningLabel))
                    .Append("</div>");
            }

            if (line.LiveStream)
            {
                sb.Append("<p class=\"reasoning-body\">")
                    .Append(Esc(ChatRichTextDisplayNormalizer.Normalize(line.ReasoningText)))
                    .Append("<span class=\"stream-caret\">▍</span></p>");
            }
            else if (!string.IsNullOrWhiteSpace(line.ReasoningText))
            {
                sb.Append(BuildBodyWithLinks(line.ReasoningText, sentenceBreaks: true));
            }

            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(line.BodyText))
        {
            sb.Append(line.LiveStream
                ? BuildStreamingBody(line.BodyText, line.ApplySentenceBreaks)
                : BuildBodyWithLinks(line.BodyText, line.ApplySentenceBreaks));
        }

        sb.Append("</article>");
        return sb.ToString();
    }

    /// <summary>段落内の http(s) を &lt;a&gt; にする（選択は DOM のまま）。</summary>
    public static string BuildBodyWithLinks(string? sourceText, bool sentenceBreaks)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return string.Empty;

        var normalized = ChatRichTextDisplayNormalizer.Normalize(sourceText);
        var blocks = ChatRichContentParser.ParseBlocks(normalized, sentenceBreaks);
        if (blocks.Count == 0)
            return $"<p>{LinkifyEscaped(normalized)}</p>";

        var sb = new StringBuilder();
        foreach (var block in blocks)
            AppendRichBlock(sb, block);

        return sb.Length > 0 ? sb.ToString() : $"<p>{ChatInlineMarkupHtml.Format(normalized)}</p>";
    }

    /// <summary>閉じたブロックはリッチ。最後の未閉じ（伸びてるリスト・表・段落）はプレーン。</summary>
    private static string BuildStreamingBody(string? sourceText, bool sentenceBreaks)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return string.Empty;

        var normalized = ChatRichTextDisplayNormalizer.Normalize(sourceText);
        var blocks = ChatRichContentParser.ParseBlocks(normalized, sentenceBreaks);
        if (blocks.Count == 0)
        {
            return "<p class=\"reasoning-body\">"
                + Esc(normalized)
                + "</p>";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var last = i == blocks.Count - 1;
            if (!last || IsClosedTail(blocks[i]))
                AppendRichBlock(sb, blocks[i]);
            else
            {
                sb.Append("<p class=\"reasoning-body\">")
                    .Append(Esc(FormatBlockPlain(blocks[i])))
                    .Append("</p>");
            }
        }

        return sb.Length > 0
            ? sb.ToString()
            : "<p class=\"reasoning-body\">" + Esc(normalized) + "</p>";
    }

    private static bool IsClosedTail(ChatDisplayBlock block) =>
        block.Kind == ChatDisplayBlockKind.Code
        || (block.Kind == ChatDisplayBlockKind.Table && block.TableRows.Count >= 1);

    private static int OrderedListStart(ChatDisplayBlock block) =>
        block.ListStartNumber > 0 ? block.ListStartNumber : 1;

    private static string ListMark(bool ordered, int number, int itemCount)
    {
        if (!ordered)
            return "・ ";
        if (itemCount >= 2)
            return $"・{number}. ";
        return $"{number}. ";
    }

    private static string FormatBlockPlain(ChatDisplayBlock block)
    {
        switch (block.Kind)
        {
            case ChatDisplayBlockKind.List:
                if (!block.ListOrdered)
                    return string.Join('\n', block.ListItems.Select(item => "- " + item));
                var plainStart = OrderedListStart(block);
                return string.Join('\n', block.ListItems.Select((item, i) =>
                    ListMark(ordered: true, plainStart + i, block.ListItems.Count) + item));
            case ChatDisplayBlockKind.Table:
                var lines = new List<string>();
                if (block.TableHeader.Count > 0)
                {
                    lines.Add("| " + string.Join(" | ", block.TableHeader) + " |");
                    lines.Add("| " + string.Join(" | ", block.TableHeader.Select(_ => "---")) + " |");
                }

                foreach (var row in block.TableRows)
                    lines.Add("| " + string.Join(" | ", row) + " |");
                return string.Join('\n', lines);
            case ChatDisplayBlockKind.Code:
                return block.CodeText;
            default:
                return string.Join('\n', block.ParagraphLines);
        }
    }

    private static void AppendRichBlock(StringBuilder sb, ChatDisplayBlock block)
    {
        switch (block.Kind)
        {
            case ChatDisplayBlockKind.Paragraph:
                foreach (var paragraphLine in block.ParagraphLines)
                {
                    if (paragraphLine.Length > 0)
                        sb.Append("<p>").Append(ChatInlineMarkupHtml.Format(paragraphLine)).Append("</p>");
                }
                break;
            case ChatDisplayBlockKind.List:
                var start = OrderedListStart(block);
                if (!block.ListOrdered)
                    sb.Append("<ul class=\"chat-list\">");
                else if (start == 1)
                    sb.Append("<ol class=\"chat-list\">");
                else
                    sb.Append("<ol class=\"chat-list\" start=\"").Append(start).Append("\">");
                for (var i = 0; i < block.ListItems.Count; i++)
                {
                    var mark = ListMark(block.ListOrdered, start + i, block.ListItems.Count);
                    sb.Append("<li><span class=\"list-mark\">")
                        .Append(Esc(mark))
                        .Append("</span>")
                        .Append(ChatInlineMarkupHtml.Format(block.ListItems[i]))
                        .Append("</li>");
                }

                sb.Append(block.ListOrdered ? "</ol>" : "</ul>");
                break;
            case ChatDisplayBlockKind.Table:
                sb.Append(BuildTableHtml(block.TableHeader, block.TableRows));
                break;
            case ChatDisplayBlockKind.Code:
                sb.Append("<div class=\"chat-code-wrap\">")
                    .Append("<button type=\"button\" class=\"chat-copy\">コピー</button>")
                    .Append("<pre class=\"chat-code\">")
                    .Append(Esc(block.CodeText))
                    .Append("</pre></div>");
                break;
        }
    }

    private static string BuildTableHtml(
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
            var cell = c < header.Count ? cellSanitize(header[c]) : string.Empty;
            sb.Append("<th>").Append(ChatInlineMarkupHtml.Format(cell)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            for (var c = 0; c < columnCount; c++)
            {
                var cell = c < row.Count ? cellSanitize(row[c]) : string.Empty;
                sb.Append("<td>").Append(ChatInlineMarkupHtml.Format(cell)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();

        static string cellSanitize(string? text) => ChatTableBoxFormatter.SanitizeCell(text);
    }

    internal static string LinkifyEscaped(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
            return Esc(text);

        var sb = new StringBuilder();
        foreach (var segment in ChatMessageUrlExtractor.SplitByUrls(text))
        {
            if (segment.IsUrl
                && Uri.TryCreate(segment.Text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                sb.Append("<a href=\"")
                    .Append(Esc(uri.AbsoluteUri))
                    .Append("\">")
                    .Append(Esc(segment.Text))
                    .Append("</a>");
            }
            else
            {
                sb.Append(Esc(segment.Text));
            }
        }

        return sb.ToString();
    }

    private static string Esc(string text) => WebUtility.HtmlEncode(text);
}
