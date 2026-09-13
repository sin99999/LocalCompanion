using LocalCompanion.Core.Tests.Fixtures;
using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>ゴールデン資料集から LLM なしでグラフを作る。source 削除で辺と概念が減ること、再構築版の判定も見る。</summary>
public sealed class RagGraphIndexerTests : IDisposable
{
    private readonly SqliteConnection _conn = RagGoldenCorpus.OpenFilled();
    private readonly RagSqliteGraph _graph = new();
    private readonly RagGraphIndexer _indexer;

    public RagGraphIndexerTests()
    {
        _graph.EnsureTables(_conn);
        _indexer = new RagGraphIndexer(_graph, new HeuristicWordSegmenter());
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void RebuildAll_BuildsConceptsEdgesAndCommunities_AndMarksVersion()
    {
        Assert.True(_indexer.NeedsRebuild(_conn));

        _indexer.RebuildAll(_conn);

        var stats = _graph.GetStats(_conn);
        Assert.True(stats.ConceptCount > 20, $"concepts={stats.ConceptCount}");
        Assert.True(stats.EdgeCount > 5, $"edges={stats.EdgeCount}");
        Assert.True(stats.CommunityCount >= 2, $"communities={stats.CommunityCount}");
        Assert.NotNull(stats.BuiltAt);
        Assert.False(_indexer.NeedsRebuild(_conn));

        var concepts = _graph.LoadConcepts(_conn);
        var theft = Assert.Single(concepts, c => c.NormTerm == "第235条");
        Assert.Equal("article", theft.Kind);
        Assert.Contains(concepts, c => c.NormTerm == "ビルド");
        Assert.Contains(concepts, c => c.NormTerm == "webview2");
    }

    [Fact]
    public void RebuildAll_TheftCorpus_HasRefersEdgeFromAttemptArticleToTheftArticle()
    {
        _indexer.RebuildAll(_conn);

        var ids = _graph.LoadConcepts(_conn).ToDictionary(c => c.NormTerm, c => c.Id);
        var a243 = ids["第243条"];
        var a235 = ids["第235条"];
        var (lo, hi) = a243 < a235 ? (a243, a235) : (a235, a243);

        var refers = _graph.LoadAggregatedEdges(_conn)
            .Where(e => e.Kind == RagConceptEdgeDraft.KindRefers)
            .ToList();
        Assert.Contains(refers, e => e.A == lo && e.B == hi);
    }

    [Fact]
    public void DeleteSource_RemovesItsEdgesAndOrphanConcepts_KeepsShared()
    {
        _indexer.RebuildAll(_conn);
        var before = _graph.GetStats(_conn);
        Assert.Contains(_graph.LoadConcepts(_conn), c => c.NormTerm == "webview2");

        using (var tx = _conn.BeginTransaction())
        {
            _graph.DeleteSource(_conn, RagGoldenCorpus.ReadmeSource, tx);
            var del = _conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM rag_chunks WHERE source = $s";
            del.Parameters.AddWithValue("$s", RagGoldenCorpus.ReadmeSource);
            del.ExecuteNonQuery();
            _indexer.AfterSourceRemoved(_conn, tx);
            tx.Commit();
        }

        var after = _graph.GetStats(_conn);
        Assert.True(after.ConceptCount < before.ConceptCount);
        Assert.True(after.EdgeCount < before.EdgeCount);
        var concepts = _graph.LoadConcepts(_conn);
        Assert.DoesNotContain(concepts, c => c.NormTerm == "webview2");
        Assert.Contains(concepts, c => c.NormTerm == "第235条");
    }

    [Fact]
    public void IndexSource_Twice_IsIdempotent()
    {
        _indexer.RebuildAll(_conn);
        var first = _graph.LoadAggregatedEdges(_conn);

        _indexer.IndexSource(_conn, RagGoldenCorpus.TheftSource);
        var second = _graph.LoadAggregatedEdges(_conn);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Sum(e => e.Weight), second.Sum(e => e.Weight), 6);
    }

    [Fact]
    public void BuildSource_ChunkConceptsAreWrittenPerChunk()
    {
        _indexer.RebuildAll(_conn);

        var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM rag_chunk_concepts cc
            INNER JOIN rag_chunks c ON c.id = cc.chunk_id
            WHERE c.source = $s
            """;
        cmd.Parameters.AddWithValue("$s", RagGoldenCorpus.ReadmeSource);
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 5);

        var orphans = _conn.CreateCommand();
        orphans.CommandText = "SELECT COUNT(*) FROM rag_chunk_concepts WHERE chunk_id NOT IN (SELECT id FROM rag_chunks)";
        Assert.Equal(0, Convert.ToInt32(orphans.ExecuteScalar()));
    }
}
