using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>概念抽出（LLM なし）。見出し・条番号は重く、機能語・1 文字・数字だけは落ちる。</summary>
public sealed class RagConceptExtractorTests
{
    private static readonly RagConceptExtractor Extractor = new(new HeuristicWordSegmenter());

    private static RagGraphChunkInput Chunk(long id, string header, string text, int articleMain = 0, string entryKey = "") =>
        new(id, "doc.md", header, text, entryKey, articleMain, "");

    [Fact]
    public void Extract_LegalChunk_ArticleAndHeadingTermsRankHigh()
    {
        var chunk = Chunk(1, "第235条（窃盗）",
            "他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。", articleMain: 235);

        var concepts = Extractor.Extract(chunk);
        var byNorm = concepts.ToDictionary(c => c.NormTerm);

        Assert.Equal(RagConceptKind.Article, byNorm["第235条"].Kind);
        Assert.Equal(RagConceptKind.Heading, byNorm["窃盗"].Kind);
        Assert.True(byNorm["窃盗"].Count >= 3, "見出し(+2) と本文(+1)");
        Assert.Contains("財物", byNorm.Keys);
        Assert.Contains("懲役", byNorm.Keys);
        Assert.DoesNotContain("者", byNorm.Keys);
        Assert.DoesNotContain("第", byNorm.Keys);
        Assert.Equal(concepts.OrderByDescending(c => c.Count).ThenBy(c => c.NormTerm, StringComparer.Ordinal).Select(c => c.NormTerm),
            concepts.Select(c => c.NormTerm));
    }

    [Fact]
    public void Extract_ReadmeChunk_LatinLowercased_StopwordsDropped()
    {
        var chunk = Chunk(2, "ビルド", "リポジトリのルートで dotnet build を実行します。Platform は x64 を指定します。WebView2 Runtime が必要です。");

        var norms = Extractor.Extract(chunk).Select(c => c.NormTerm).ToList();

        Assert.Contains("ビルド", norms);
        Assert.Contains("dotnet", norms);
        Assert.Contains("platform", norms);
        Assert.Contains("x64", norms);
        Assert.Contains("webview2", norms);
        Assert.DoesNotContain("実行", norms);
        Assert.DoesNotContain("必要", norms);
    }

    [Fact]
    public void Extract_EntryKey_BecomesEntryConcept()
    {
        var chunk = Chunk(3, "", "使用者とは、事業主又は事業の経営担当者をいう。", entryKey: "使用者");

        var concepts = Extractor.Extract(chunk);

        var entry = Assert.Single(concepts, c => c.NormTerm == "使用者");
        Assert.Equal(RagConceptKind.Entry, entry.Kind);
        Assert.True(entry.Count >= 4);
    }

    [Fact]
    public void Normalize_NfkcLowercaseAndDropsMiddleDot()
    {
        Assert.Equal("webview2", RagConceptExtractor.Normalize("ＷｅｂＶｉｅｗ２"));
        Assert.Equal("労働時間", RagConceptExtractor.Normalize("労働・時間"));
        Assert.Equal("dotnetbuild", RagConceptExtractor.Normalize(" dotnet build "));
    }

    [Fact]
    public void ApplyDocRatioCap_DropsTermsInMostChunks_KeepsArticles()
    {
        var chunks = Enumerable.Range(1, 10).Select(i => new RagChunkConcepts(i,
        [
            new RagConceptOccurrence("共通", "共通", RagConceptKind.Term, 1),
            new RagConceptOccurrence($"第{i}条", $"第{i}条", RagConceptKind.Article, 3),
            new RagConceptOccurrence(i <= 2 ? "固有" : $"語{i}", i <= 2 ? "固有" : $"語{i}", RagConceptKind.Term, 1),
        ])).ToList();

        var capped = RagConceptExtractor.ApplyDocRatioCap(chunks);

        Assert.All(capped, c => Assert.DoesNotContain(c.Concepts, x => x.NormTerm == "共通"));
        Assert.Contains(capped[0].Concepts, x => x.NormTerm == "第1条");
        Assert.Contains(capped[0].Concepts, x => x.NormTerm == "固有");
    }

    [Fact]
    public void ApplyDocRatioCap_SmallCorpus_Untouched()
    {
        var chunks = Enumerable.Range(1, 3).Select(i => new RagChunkConcepts(i,
            [new RagConceptOccurrence("共通", "共通", RagConceptKind.Term, 1)])).ToList();

        var capped = RagConceptExtractor.ApplyDocRatioCap(chunks);

        Assert.All(capped, c => Assert.Contains(c.Concepts, x => x.NormTerm == "共通"));
    }

    [Fact]
    public void TopTerms_TakesLeadingK()
    {
        var concepts = Enumerable.Range(1, 20)
            .Select(i => new RagConceptOccurrence($"t{i}", $"t{i}", RagConceptKind.Term, 21 - i))
            .ToList();

        var top = RagConceptExtractor.TopTerms(concepts, 5);

        Assert.Equal(["t1", "t2", "t3", "t4", "t5"], top);
    }
}
