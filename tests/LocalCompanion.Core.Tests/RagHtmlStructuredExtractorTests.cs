using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagHtmlStructuredExtractorTests
{
    [Fact]
    public void ToStructuredMarkdown_ConvertsHeadings()
    {
        const string html = """
            <html><body>
            <h1>就業規則</h1>
            <p>第1条 目的</p>
            <h2>副業</h2>
            <p>禁止する。</p>
            </body></html>
            """;

        var md = RagHtmlStructuredExtractor.ToStructuredMarkdown(html);
        Assert.Contains("# 就業規則", md);
        Assert.Contains("## 副業", md);
        Assert.Contains("禁止", md);
    }
}
