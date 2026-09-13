using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>共起は文 3 つの窓で数え、弱い辺は捨てる。数えるのは上位語だけ。</summary>
public sealed class RagCooccurrenceGraphBuilderTests
{
    private static readonly RagCooccurrenceGraphBuilder Builder = new(new HeuristicWordSegmenter());

    private const string LaborText =
        "労働時間は一日八時間。使用者は労働時間を守る。休憩は労働時間の途中に与える。賃金は毎月払う。";

    private static RagGraphChunkInput Chunk(string text, string header = "") =>
        new(1, "labor.md", header, text, "", 0, "");

    [Fact]
    public void Build_SentenceWindow_CountsPairsAndPrunesWeakOnes()
    {
        var edges = Builder.Build([(Chunk(LaborText), ["労働時間", "使用者", "休憩", "賃金"])], minEdgeWeight: 2);

        var keys = edges.Select(e => (e.NormA, e.NormB)).ToList();
        Assert.Contains(("使用者", "労働時間"), keys);
        Assert.Contains(("休憩", "労働時間"), keys);
        Assert.Contains(("休憩", "使用者"), keys);
        Assert.DoesNotContain(keys, k => k.NormA == "賃金" || k.NormB == "賃金");
        Assert.All(edges, e => Assert.Equal(RagConceptEdgeDraft.KindCooccurrence, e.Kind));
        Assert.All(edges, e => Assert.True(string.CompareOrdinal(e.NormA, e.NormB) < 0));
    }

    [Fact]
    public void Build_OnlyTopTermsAreConsidered()
    {
        var edges = Builder.Build([(Chunk(LaborText), ["労働時間", "使用者"])], minEdgeWeight: 1);

        Assert.Single(edges);
        Assert.Equal(("使用者", "労働時間"), (edges[0].NormA, edges[0].NormB));
    }

    [Fact]
    public void Build_HeaderTermsJoinEveryWindow_WithHeaderWeight()
    {
        var edges = Builder.Build([(Chunk("賃金は毎月払う。休憩は途中に与える。", header: "労働条件"), ["労働条件", "賃金", "休憩"])], minEdgeWeight: 1);

        var headerWage = Assert.Single(edges, e => (e.NormA, e.NormB) == ("労働条件", "賃金"));
        Assert.Equal(RagCooccurrenceGraphBuilder.HeaderPairWeight, headerWage.Weight);
        Assert.Contains(edges, e => (e.NormA, e.NormB) == ("休憩", "労働条件"));
        var body = Assert.Single(edges, e => (e.NormA, e.NormB) == ("休憩", "賃金"));
        Assert.Equal(1.0, body.Weight);
    }

    /// <summary>条文 1 文のチャンクでも、見出し語↔本文語の辺は既定の枝刈り（2）を生き残る。</summary>
    [Fact]
    public void Build_SingleSentenceArticle_KeepsHeaderBodyEdgesAtDefaultPrune()
    {
        var chunk = new RagGraphChunkInput(1, "刑法.md", "第235条（窃盗）",
            "他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。", "", 235, "legal");

        var edges = Builder.Build([(chunk, ["窃盗", "財物", "窃取", "懲役", "罰金"])]);

        Assert.Contains(edges, e => (e.NormA, e.NormB) == ("窃盗", "財物"));
        Assert.Contains(edges, e => (e.NormA, e.NormB) == ("窃盗", "罰金"));
        Assert.DoesNotContain(edges, e => (e.NormA, e.NormB) == ("懲役", "財物"));
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var a = Builder.Build([(Chunk(LaborText), ["労働時間", "使用者", "休憩", "賃金"])], minEdgeWeight: 1);
        var b = Builder.Build([(Chunk(LaborText), ["労働時間", "使用者", "休憩", "賃金"])], minEdgeWeight: 1);

        Assert.Equal(a, b);
    }
}
