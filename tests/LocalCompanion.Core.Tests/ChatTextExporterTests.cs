using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatTextExporterTests
{
    [Fact]
    public void TryExport_WritesMarkdownToTempDesktop()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var request = new ChatExportRequest(
                "刑法第54条",
                "test-export",
                ".md",
                new ChatExportTarget(ChatExportTargetKind.Desktop));

            var document = new ChatExportDocument("刑法第54条", "第54条の本文です。");
            var content = ChatTextExporter.BuildDocument(
                document.Body,
                request,
                document.Title,
                ["刑法.md"],
                japanese: true);

            Assert.Contains("# 刑法第54条", content);
            Assert.Contains("第54条の本文です。", content);
            Assert.Contains("刑法.md", content);

            var path = Path.Combine(dir, "test-export.md");
            AtomicFile.WriteAllText(path, content);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData(".xml")]
    [InlineData(".yaml")]
    [InlineData(".html")]
    public void ExportFormats_AreAllowed(string ext)
    {
        Assert.True(ChatTextExportFormats.IsAllowed(ext));
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData(".py")]
    [InlineData(".exe")]
    public void ExportFormats_BlockCodeExtensions(string ext)
    {
        Assert.False(ChatTextExportFormats.IsAllowed(ext));
    }
}
