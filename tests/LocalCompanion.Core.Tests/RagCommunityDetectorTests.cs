using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>決定的ラベル伝播: 2 つの三角形は 2 束、孤立ノードは 1 ノード 1 束、2 回回して同じ結果。</summary>
public sealed class RagCommunityDetectorTests
{
    private static readonly List<RagCommunityDetector.Node> Nodes =
        Enumerable.Range(1, 7).Select(i => new RagCommunityDetector.Node(i, $"n{i}")).ToList();

    private static readonly List<(long A, long B, double Weight)> TwoTriangles =
    [
        (1, 2, 3), (2, 3, 3), (1, 3, 3),
        (4, 5, 3), (5, 6, 3), (4, 6, 3),
        (3, 4, 0.5),
    ];

    [Fact]
    public void Detect_TwoTrianglesWithWeakBridge_TwoLevel0Communities_IsolatedNodeAlone()
    {
        var communities = RagCommunityDetector.Detect(Nodes, TwoTriangles);

        var level0 = communities.Where(c => c.Level == 0).ToList();
        Assert.Equal(3, level0.Count);
        Assert.Contains(level0, c => c.MemberConceptIds.SequenceEqual([1L, 2L, 3L]));
        Assert.Contains(level0, c => c.MemberConceptIds.SequenceEqual([4L, 5L, 6L]));
        Assert.Contains(level0, c => c.MemberConceptIds.SequenceEqual([7L]));
        Assert.All(level0, c => Assert.NotNull(c.ParentId));
        Assert.All(level0, c => Assert.Equal(c.MemberConceptIds.Count, c.Size));
    }

    [Fact]
    public void Detect_Level1_ParentsExistAndCoverAllConcepts()
    {
        var communities = RagCommunityDetector.Detect(Nodes, TwoTriangles);

        var level1 = communities.Where(c => c.Level == 1).ToList();
        Assert.NotEmpty(level1);
        Assert.True(level1.Count <= communities.Count(c => c.Level == 0));
        Assert.Equal(7, level1.Sum(c => c.Size));
        foreach (var child in communities.Where(c => c.Level == 0))
            Assert.Contains(level1, p => p.Id == child.ParentId);
        Assert.Equal(communities.Count, communities.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Detect_Deterministic_AcrossRuns()
    {
        var a = RagCommunityDetector.Detect(Nodes, TwoTriangles);
        var b = RagCommunityDetector.Detect(Nodes, TwoTriangles);

        Assert.Equal(a.Select(c => (c.Id, c.Level, c.ParentId, c.Label, string.Join(",", c.MemberConceptIds))),
            b.Select(c => (c.Id, c.Level, c.ParentId, c.Label, string.Join(",", c.MemberConceptIds))));
    }

    [Fact]
    public void Detect_Label_UsesTopDegreeTermsAndHeader()
    {
        var headers = new Dictionary<long, string> { [1] = "第1章", [2] = "第1章", [3] = "第2章" };
        var communities = RagCommunityDetector.Detect(Nodes, TwoTriangles, headers);

        var triangle = communities.First(c => c.Level == 0 && c.MemberConceptIds.Contains(1L));
        Assert.StartsWith("n3・", triangle.Label);
        Assert.EndsWith("（第1章）", triangle.Label);
    }

    /// <summary>実 DB で起きた「ハブ語が全部を 1 束に溶かす」再現: 星形 120 ノードでも level 0 は上限以下に割れる。</summary>
    [Fact]
    public void Detect_HubStar_Level0CommunitiesRespectSizeCap()
    {
        var nodes = Enumerable.Range(1, 121).Select(i => new RagCommunityDetector.Node(i, $"n{i}")).ToList();
        var edges = Enumerable.Range(2, 120).Select(i => ((long)1, (long)i, 3.0)).ToList();
        // 葉どうしも少し繋いで密にする
        for (var i = 2; i < 121; i++)
            edges.Add((i, i + 1, 1.0));

        var communities = RagCommunityDetector.Detect(nodes, edges);

        var level0 = communities.Where(c => c.Level == 0).ToList();
        Assert.All(level0, c => Assert.True(c.Size <= RagCommunityDetector.Level0MaxSize, $"size {c.Size}"));
        Assert.True(level0.Count >= 121 / RagCommunityDetector.Level0MaxSize, $"level0 count {level0.Count}");
        Assert.Equal(121, level0.Sum(c => c.Size));

        var level1 = communities.Where(c => c.Level == 1).ToList();
        Assert.All(level1, c => Assert.True(c.Size <= RagCommunityDetector.Level1MaxSize));
        Assert.True(level1.Count < level0.Count, $"level1 {level1.Count} should merge level0 {level0.Count}");
    }

    [Fact]
    public void Detect_NoEdges_EveryNodeIsItsOwnCommunity()
    {
        var communities = RagCommunityDetector.Detect(Nodes, []);

        Assert.Equal(7, communities.Count(c => c.Level == 0));
        Assert.Equal(7, communities.Count(c => c.Level == 1));
        Assert.Empty(RagCommunityDetector.Detect([], []));
    }
}
