using LocalCompanion.Localization;
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

    [Fact]
    public void Registry_RejectsOversizedFile_WhenLimitSet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lc-rag-limit-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "xx");
            var registry = new RagDocumentReaderRegistry(usePdfLayoutReader: false, maxFileBytes: 1);
            var ex = Assert.Throws<LocalizedServiceException>(() => registry.ReadText(path));
            Assert.Equal("Settings.Rag.Error.FileTooLargeNamed", ex.LocalizationKey);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Registry_AllowsAnySize_WhenLimitUnset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lc-rag-nolimit-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, new string('a', 4096));
            var registry = new RagDocumentReaderRegistry(usePdfLayoutReader: false, maxFileBytes: 0);
            Assert.Equal(4096, registry.ReadText(path).Length);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
