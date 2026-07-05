using System.IO.Compression;
using System.Xml.Linq;
using LocalCompanion.Localization;

namespace LocalCompanion.Services.DocumentReading;

internal sealed class DocxDocumentReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".docx"];

    public string ReadFromPath(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadFromStream(stream, path);
    }

    public string ReadFromStream(Stream stream, string fileName)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new LocalizedServiceException("Settings.Rag.Error.WordBodyMissing");
        using var xmlStream = entry.Open();
        var doc = XDocument.Load(xmlStream);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var paragraphs = doc.Descendants(ns + "p");
        var lines = new List<string>();
        foreach (var p in paragraphs)
        {
            var text = string.Concat(p.Descendants(ns + "t").Select(x => x.Value));
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add(text.Trim());
        }

        return lines.Count > 0 ? string.Join("\n", lines) : string.Join("\n",
            doc.Descendants(ns + "t").Select(x => x.Value).Where(v => v.Length > 0));
    }
}
