using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportDocumentFormatterTests
{
    [Fact]
    public void TryParseResponse_ParsesJsonObject()
    {
        var raw = """{"title":"刑法第54条","body":"## 条文\n\n第54条 …"}""";
        Assert.True(ChatExportDocumentFormatter.TryParseResponse(raw, out var doc));
        Assert.Equal("刑法第54条", doc.Title);
        Assert.Contains("第54条", doc.Body);
    }

    [Fact]
    public void TryParseResponse_ParsesFencedJson()
    {
        var raw = """
                  Here is the result:
                  ```json
                  {"title":"労基法メモ","body":"## 残業\n\n1. …"}
                  ```
                  """;
        Assert.True(ChatExportDocumentFormatter.TryParseResponse(raw, out var doc));
        Assert.Equal("労基法メモ", doc.Title);
    }

    [Fact]
    public void TryParseResponse_RejectsMissingBody()
    {
        Assert.False(ChatExportDocumentFormatter.TryParseResponse("""{"title":"only"}""", out _));
    }
}
