using LocalCompanion.Services;
using LocalCompanion.Services.DocumentReading;

namespace LocalCompanion.Core.Tests;

public sealed class RagDocumentReaderRegistryTests
{
    [Fact]
    public void Registry_ReadsPlainText()
    {
        var registry = new RagDocumentReaderRegistry(usePdfLayoutReader: false);
        Assert.True(registry.IsSupported("sample.md"));
    }

    [Fact]
    public void Configure_SwitchesPdfReaderMode()
    {
        RagDocumentReader.Configure(false);
        Assert.True(RagDocumentReader.IsSupported("doc.pdf"));
        RagDocumentReader.Configure(true);
        Assert.True(RagDocumentReader.IsSupported("doc.pdf"));
    }
}
