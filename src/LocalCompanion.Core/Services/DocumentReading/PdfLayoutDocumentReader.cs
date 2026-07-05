using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LocalCompanion.Services.DocumentReading;

internal sealed class PdfLayoutDocumentReader : IDocumentReader
{
    private static readonly Regex ArticleHeader = new(@"^第\s*\d+\s*条", RegexOptions.Compiled);
    private static readonly Regex ChapterHeader = new(@"^第\s*\d+\s*章", RegexOptions.Compiled);

    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public string ReadFromPath(string path)
    {
        using var doc = PdfDocument.Open(path);
        return ExtractLayout(doc);
    }

    public string ReadFromStream(Stream stream, string fileName)
    {
        using var doc = PdfDocument.Open(stream);
        return ExtractLayout(doc);
    }

    internal static string ExtractLayout(PdfDocument doc)
    {
        var pages = doc.GetPages().ToList();
        if (pages.Count == 0)
            return "";

        var pageLines = pages.Select(ExtractPageLines).ToList();
        var headerFooter = DetectRepeatedEdgeLines(pageLines);
        var sb = new StringBuilder();

        for (var i = 0; i < pages.Count; i++)
        {
            sb.AppendLine($"--- ページ {pages[i].Number} ---");
            foreach (var line in pageLines[i])
            {
                if (headerFooter.Contains(NormalizeLine(line.Text)))
                    continue;

                var prefix = line.HeadingLevel switch
                {
                    1 => "# ",
                    2 => "## ",
                    3 => "### ",
                    _ => "",
                };
                sb.AppendLine(prefix + line.Text.Trim());
            }
        }

        return sb.ToString();
    }

    private static List<LayoutLine> ExtractPageLines(Page page)
    {
        var words = page.GetWords().OrderBy(w => -w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).ToList();
        if (words.Count == 0)
            return [];

        var bodySize = words
            .SelectMany(w => w.Letters)
            .Select(l => l.FontSize)
            .Where(s => s > 0)
            .DefaultIfEmpty(12)
            .OrderBy(s => s)
            .ElementAt(words.Count / 2);

        var lines = new List<LayoutLine>();
        var current = new List<Word>();
        var currentY = words[0].BoundingBox.Top;

        foreach (var word in words)
        {
            if (current.Count > 0 && Math.Abs(word.BoundingBox.Top - currentY) > 4)
            {
                lines.Add(BuildLine(current, bodySize));
                current = [];
            }

            current.Add(word);
            currentY = word.BoundingBox.Top;
        }

        if (current.Count > 0)
            lines.Add(BuildLine(current, bodySize));

        return lines;
    }

    private static LayoutLine BuildLine(IReadOnlyList<Word> words, double bodySize)
    {
        var text = string.Join(" ", words.Select(w => w.Text)).Trim();
        var maxSize = words.SelectMany(w => w.Letters).Select(l => l.FontSize).DefaultIfEmpty(bodySize).Max();
        var heading = 0;
        if (ChapterHeader.IsMatch(text))
            heading = 1;
        else if (ArticleHeader.IsMatch(text))
            heading = 2;
        else if (maxSize >= bodySize * 1.18)
            heading = text.Length <= 60 ? 2 : 3;

        return new LayoutLine(text, heading);
    }

    private static HashSet<string> DetectRepeatedEdgeLines(IReadOnlyList<List<LayoutLine>> pageLines)
    {
        var topCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var bottomCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lines in pageLines)
        {
            if (lines.Count == 0)
                continue;

            var top = NormalizeLine(lines[0].Text);
            var bottom = NormalizeLine(lines[^1].Text);
            if (top.Length >= 3)
                topCounts[top] = topCounts.GetValueOrDefault(top) + 1;
            if (bottom.Length >= 3)
                bottomCounts[bottom] = bottomCounts.GetValueOrDefault(bottom) + 1;
        }

        var threshold = Math.Max(2, pageLines.Count * 3 / 5);
        var repeated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, count) in topCounts)
        {
            if (count >= threshold)
                repeated.Add(line);
        }

        foreach (var (line, count) in bottomCounts)
        {
            if (count >= threshold)
                repeated.Add(line);
        }

        return repeated;
    }

    private static string NormalizeLine(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ");

    private sealed record LayoutLine(string Text, int HeadingLevel);
}
