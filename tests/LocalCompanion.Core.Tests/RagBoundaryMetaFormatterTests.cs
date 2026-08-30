using LocalCompanion.Localization;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagBoundaryMetaFormatterTests
{
    public static TheoryData<AppLanguage> AllUiLanguages()
    {
        var data = new TheoryData<AppLanguage>();
        foreach (var lang in Enum.GetValues<AppLanguage>())
            data.Add(lang);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllUiLanguages))]
    public void Format_DoesNotExposeInternalSortKey(AppLanguage language)
    {
        var last = RagBoundaryMetaFormatter.Format(
            RagArticleBoundaryIntent.Last,
            "刑法.md",
            "第264条",
            language);
        var first = RagBoundaryMetaFormatter.Format(
            RagArticleBoundaryIntent.First,
            "刑法.md",
            "第1条",
            language);

        Assert.DoesNotContain("article_sort_key", last, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("article_sort_key", first, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("刑法.md", last, StringComparison.Ordinal);
        Assert.Contains("第264条", last, StringComparison.Ordinal);
        Assert.Contains("第1条", first, StringComparison.Ordinal);
        Assert.NotEqual(last, first);
    }

    [Fact]
    public void Format_JapaneseLast_UsesLastWordingNotInternalKey()
    {
        var text = RagBoundaryMetaFormatter.Format(
            RagArticleBoundaryIntent.Last,
            "刑法.md",
            "第264条",
            AppLanguage.Japanese);

        Assert.Contains("最後", text, StringComparison.Ordinal);
        Assert.DoesNotContain("最終", text, StringComparison.Ordinal);
        Assert.DoesNotContain("article_sort_key", text, StringComparison.Ordinal);
        Assert.EndsWith("です。", text);
    }

    [Fact]
    public void Format_EnglishLast_UsesLastWording()
    {
        var text = RagBoundaryMetaFormatter.Format(
            RagArticleBoundaryIntent.Last,
            "刑法.md",
            "Article 264",
            AppLanguage.English);

        Assert.Contains("last", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("article_sort_key", text, StringComparison.OrdinalIgnoreCase);
    }
}
