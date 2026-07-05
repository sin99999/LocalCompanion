namespace LocalCompanion.Services.DocumentReading;

internal interface IDocumentReader
{
    IReadOnlyCollection<string> Extensions { get; }
    string ReadFromPath(string path);
    string ReadFromStream(Stream stream, string fileName);
}
