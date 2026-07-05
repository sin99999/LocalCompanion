using System.Text;
using LocalCompanion.Services;
using LocalCompanion.Services.DocumentReading;

namespace LocalCompanion.Core.Tests;

public sealed class RagTextEncodingTests
{
    [Fact]
    public void DetectFromBytes_Utf8JapaneseMd_IsNotShiftJis()
    {
        var path = @"C:\Users\SIN\Desktop\PDF\刑法.md";
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var encoding = RagTextEncoding.DetectFromBytes(bytes);
        Assert.Equal(Encoding.UTF8, encoding);

        var text = encoding.GetString(bytes);
        Assert.Contains("第1条", text, StringComparison.Ordinal);
        Assert.Contains("#### 第54条", text, StringComparison.Ordinal);
        Assert.DoesNotContain("隨ｬ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentReader_PenalCodeMd_PreservesArticleHeaders()
    {
        var path = @"C:\Users\SIN\Desktop\PDF\刑法.md";
        if (!File.Exists(path))
            return;

        RagDocumentReader.Configure(usePdfLayoutReader: false);
        var text = RagDocumentReader.ReadText(path);
        Assert.Contains("第8条（他の法令の罪に対する適用）", text, StringComparison.Ordinal);

        var docKind = RagDocumentProfileDetector.Detect(path, text);
        var drafts = RagStructuralChunker.CreateChunks(text, path, size: 900, overlap: 128, docKind);
        Assert.True(drafts.Count(d => d.ArticleSortKey > 0) >= 50);
    }
}
