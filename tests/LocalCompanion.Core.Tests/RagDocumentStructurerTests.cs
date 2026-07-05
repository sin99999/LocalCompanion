using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagDocumentStructurerTests
{
    [Fact]
    public void SplitWindows_LongText_ProducesMultipleParts()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 40).Select(i => $"段落{i}。" + new string('あ', 120)));
        var windows = RagDocumentStructurer.SplitWindows(text, 800);
        Assert.True(windows.Count >= 2);
        Assert.Contains("段落1", windows[0]);
    }
}
