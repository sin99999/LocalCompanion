using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagHybridSearchTests
{
    [Fact]
    public void FuseRrf_MergesBothLanes_ByReciprocalRank()
    {
        var ftsIds = new long[] { 10, 20, 30 };
        var vecIds = new long[] { 20, 40, 10 };

        var fused = RagHybridSearch.FuseRrf(ftsIds, vecIds, topK: 3, rrfK: 60, weightFts: 0.5, weightVec: 0.5);

        Assert.Equal(3, fused.Count);
        Assert.Equal(20, fused[0]);
        Assert.Contains(10, fused);
    }

    [Fact]
    public void FuseRrf_EmptyInputs_ReturnsEmpty()
    {
        var fused = RagHybridSearch.FuseRrf(Array.Empty<long>(), Array.Empty<long>(), topK: 5, rrfK: 60, 0.4, 0.6);
        Assert.Empty(fused);
    }

    [Fact]
    public void ResolveWeights_ArticleQuery_BoostsFts()
    {
        var (fts, vec) = RagHybridSearch.ResolveWeights("労基法の第8条全文", baseFts: 0.4, baseVec: 0.6);
        Assert.True(fts > vec);
    }

    [Fact]
    public void ResolveWeights_GeneralQuery_UsesBaseWeights()
    {
        var (fts, vec) = RagHybridSearch.ResolveWeights("解雇の要件について教えて", baseFts: 0.35, baseVec: 0.65);
        Assert.Equal(0.35, fts);
        Assert.Equal(0.65, vec);
    }
}
