using System.Text;
using UglyToad.PdfPig;

namespace LocalCompanion.Services.DocumentReading;

internal sealed class PdfPlainDocumentReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public string ReadFromPath(string path)
    {
        using var doc = PdfDocument.Open(path);
        return ExtractPlain(doc);
    }

    public string ReadFromStream(Stream stream, string fileName)
    {
        using var doc = PdfDocument.Open(stream);
        return ExtractPlain(doc);
    }

    internal static string ExtractPlain(PdfDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine($"--- ページ {page.Number} ---");
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }
}
