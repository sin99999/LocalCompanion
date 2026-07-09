using LocalCompanion.Localization;

namespace LocalCompanion.Services.DocumentReading;

internal sealed class RagDocumentReaderRegistry
{
    private readonly Dictionary<string, IDocumentReader> _byExt;
    private readonly TextUtf8DocumentReader _textReader;
    private readonly long _maxFileBytes;

    public RagDocumentReaderRegistry(bool usePdfLayoutReader, long maxFileBytes = 0)
    {
        _maxFileBytes = maxFileBytes;
        _textReader = new TextUtf8DocumentReader(RagDocumentReader.TextExtensionSet);
        var pdfReader = usePdfLayoutReader
            ? (IDocumentReader)new PdfLayoutDocumentReader()
            : new PdfPlainDocumentReader();

        _byExt = new Dictionary<string, IDocumentReader>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in _textReader.Extensions)
            _byExt[ext] = _textReader;
        foreach (var ext in pdfReader.Extensions)
            _byExt[ext] = pdfReader;
        var docx = new DocxDocumentReader();
        foreach (var ext in docx.Extensions)
            _byExt[ext] = docx;
    }

    public bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && _byExt.ContainsKey(ext);
    }

    public IReadOnlyList<string> SupportedExtensionList =>
        _byExt.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    public string ReadText(string path)
    {
        ValidateSize(path, new FileInfo(path).Length, path);
        var ext = Path.GetExtension(path);
        if (!_byExt.TryGetValue(ext, out var reader))
            throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", ext);
        return reader.ReadFromPath(path);
    }

    public string ReadText(Stream stream, string fileName)
    {
        if (stream.CanSeek)
            ValidateSize(fileName, stream.Length, fileName);

        var ext = Path.GetExtension(fileName);
        if (!_byExt.TryGetValue(ext, out var reader))
            throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", ext);
        return reader.ReadFromStream(stream, fileName);
    }

    private void ValidateSize(string label, long length, string pathOrName)
    {
        if (_maxFileBytes <= 0 || length <= _maxFileBytes)
            return;

        var limitMb = Math.Max(1, _maxFileBytes / (1024 * 1024));
        throw new LocalizedServiceException(
            "Settings.Rag.Error.FileTooLargeNamed",
            limitMb,
            pathOrName);
    }
}
