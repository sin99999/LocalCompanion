using LocalCompanion.Core.Tests.Fixtures;
using LocalCompanion.Data;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>グラフ第 3 レーン: 隣接語で FTS を広げ、同コミュニティのチャンクを返す。グラフが空なら何もしない。</summary>
public sealed class RagGraphLaneTests : IDisposable
{
    private readonly SqliteConnection _conn = RagGoldenCorpus.OpenFilled();
    private readonly RagSqliteGraph _graph = new();
    private readonly RagGraphLane _lane;

    public RagGraphLaneTests()
    {
        _graph.EnsureTables(_conn);
        var segmenter = new HeuristicWordSegmenter();
        new RagGraphIndexer(_graph, segmenter).RebuildAll(_conn);
        _lane = new RagGraphLane(_graph, segmenter);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void ExpandQueryTerms_TheftQuestion_ReturnsNeighborsNotTheQueryWord()
    {
        var terms = _lane.ExpandQueryTerms(_conn, "窃盗の罰則は？");

        Assert.NotEmpty(terms);
        Assert.True(terms.Count <= RagGraphLane.DefaultNeighborTerms);
        Assert.DoesNotContain(terms, t => RagConceptExtractor.Normalize(t) == "窃盗");
    }

    [Fact]
    public void ExpandQueryTerms_UnknownWords_ReturnsEmpty()
    {
        Assert.Empty(_lane.ExpandQueryTerms(_conn, "クロワッサンの焼き方"));
        Assert.Empty(_lane.ExpandQueryTerms(_conn, "ありがとう"));
    }

    [Fact]
    public void AppendToMatchQuery_AddsSegmentedPhrasesWithOr()
    {
        var match = RagGraphLane.AppendToMatchQuery("\"窃盗\"", ["財物", "WebView2"]);

        Assert.Equal("\"窃盗\" OR \"財物\" OR \"WebView2\"", match);
        Assert.Equal("\"財物\"", RagGraphLane.AppendToMatchQuery("", ["財物"]));
        Assert.Equal("\"窃盗\"", RagGraphLane.AppendToMatchQuery("\"窃盗\"", []));
    }

    [Fact]
    public void SameCommunityChunkIds_SeedFromTheftArticle_StaysInLegalSources()
    {
        var seed = ChunkId(RagGoldenCorpus.TheftSource, "第235条");
        var sameCommunity = _lane.SameCommunityChunkIds(_conn, [seed], RagGoldenCorpus.AllSources, limit: 20);

        Assert.NotEmpty(sameCommunity);
        Assert.Contains(seed, sameCommunity);
        var readmeChunks = ChunkIds(RagGoldenCorpus.ReadmeSource);
        Assert.DoesNotContain(sameCommunity, id => readmeChunks.Contains(id));
    }

    [Fact]
    public void SameCommunityChunkIds_RespectsSourceFilter()
    {
        var seed = ChunkId(RagGoldenCorpus.TheftSource, "第235条");
        var onlyReadme = _lane.SameCommunityChunkIds(_conn, [seed], [RagGoldenCorpus.ReadmeSource], limit: 20);

        Assert.Empty(onlyReadme);
    }

    [Fact]
    public void EmptyGraph_LaneIsSilent()
    {
        using var empty = RagGoldenCorpus.OpenFilled();
        _graph.EnsureTables(empty);
        var lane = new RagGraphLane(_graph, new HeuristicWordSegmenter());

        Assert.Empty(lane.ExpandQueryTerms(empty, "窃盗の罰則は？"));
        Assert.Empty(lane.SameCommunityChunkIds(empty, [1], RagGoldenCorpus.AllSources, 10));
    }

    private long ChunkId(string source, string headerPrefix)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM rag_chunks WHERE source = $s AND header_text LIKE $h || '%' ORDER BY id LIMIT 1";
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$h", headerPrefix);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private HashSet<long> ChunkIds(string source)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM rag_chunks WHERE source = $s";
        cmd.Parameters.AddWithValue("$s", source);
        var set = new HashSet<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetInt64(0));
        return set;
    }
}
