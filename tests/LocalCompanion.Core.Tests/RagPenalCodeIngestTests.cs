using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagPenalCodeIngestTests
{
    [Fact]
    public void CreateChunks_PenalCodeMarkdown_ExtractsArticleSortKeys()
    {
        var path = @"C:\Users\SIN\Desktop\PDF\刑法.md";
        if (!File.Exists(path))
        {
            // CI / other machines: use embedded sample matching real file shape
            path = null!;
        }

        var text = path is not null && File.Exists(path)
            ? File.ReadAllText(path)
            : SamplePenalCodeMd;

        var docKind = RagDocumentProfileDetector.Detect("刑法.md", text);
        Assert.Equal(RagDocumentKind.Legal, docKind);

        var drafts = RagStructuralChunker.CreateChunks(text, "刑法.md", size: 900, overlap: 128, docKind);
        var withArticle = drafts.Where(d => d.ArticleSortKey > 0).ToList();

        Assert.True(withArticle.Count >= 50, $"Expected many article chunks, got {withArticle.Count} / {drafts.Count}");

        var art8 = withArticle.FirstOrDefault(d => d.ArticleSortKey == 800);
        Assert.NotNull(art8);
        Assert.Contains("他の法令", art8.HeaderText, StringComparison.Ordinal);
        Assert.Contains("この編の規定", art8.Text, StringComparison.Ordinal);
    }

    private const string SamplePenalCodeMd = """
        # 刑法

        ## 第1編 総則

        ### 第1章 通則

        #### 第7条（定義）

        この法律において「公務員」とは、国又は地方公共団体の職員その他法令により公務に従事する議員、委員その他の職員をいう。

        #### 第8条（他の法令の罪に対する適用）

        この編の規定は、他の法令の罪についても、適用する。ただし、その法令に特別の規定があるときは、この限りでない。

        #### 第9条（刑の種類）

        死刑、懲役、禁錮、罰金、拘留及び科料を主刑とし、没収を付加刑とする。
        """;
}
