using LocalCompanion.Data;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Services;

/// <summary>
/// 概念グラフの構築を束ねる（抽出 → 参照辺 → 共起辺 → 保存 → コミュニティ再計算）。
/// LLM も埋め込みも使わないので、既存 DB は起動後に再 ingest なしで作り直せる。
/// </summary>
internal sealed class RagGraphIndexer
{
    private readonly RagSqliteGraph _graph;
    private readonly RagConceptExtractor _extractor;
    private readonly RagCooccurrenceGraphBuilder _cooccurrence;
    private readonly double _minEdgeWeight;
    private readonly int _topK;

    public RagGraphIndexer(
        RagSqliteGraph graph,
        IRagWordSegmenter segmenter,
        double minEdgeWeight = RagCooccurrenceGraphBuilder.DefaultMinEdgeWeight,
        int topK = RagConceptExtractor.DefaultTopK)
    {
        _graph = graph;
        _extractor = new RagConceptExtractor(segmenter);
        _cooccurrence = new RagCooccurrenceGraphBuilder(segmenter);
        _minEdgeWeight = minEdgeWeight;
        _topK = Math.Max(2, topK);
    }

    /// <summary>構築バージョンが古い（または未構築）なら true。</summary>
    public bool NeedsRebuild(SqliteConnection conn) =>
        _graph.GetMeta(conn, RagSqliteGraph.BuiltVersionKey) != RagSqliteGraph.BuildVersion;

    /// <summary>source 1 つ分を差し替える（rag_chunks はコミット済みであること）。コミュニティは別途。</summary>
    public void IndexSource(SqliteConnection conn, string source, SqliteTransaction? tx = null)
    {
        var chunks = LoadChunks(conn, source);
        var (concepts, edges) = BuildSource(chunks);
        _graph.ReplaceSource(conn, source, concepts, edges, tx);
    }

    /// <summary>抽出だけ（DB に触らない。テストと IndexSource が使う）。</summary>
    public (IReadOnlyList<RagChunkConcepts> Concepts, IReadOnlyList<RagConceptEdgeDraft> Edges) BuildSource(
        IReadOnlyList<RagGraphChunkInput> chunks)
    {
        var extracted = chunks
            .Select(c => new RagChunkConcepts(c.ChunkId, _extractor.Extract(c)))
            .ToList();
        var capped = RagConceptExtractor.ApplyDocRatioCap(extracted);

        var byId = capped.ToDictionary(c => c.ChunkId);
        var forCooccurrence = chunks
            .Select(c => (Chunk: c, TopTerms: RagConceptExtractor.TopTerms(byId[c.ChunkId].Concepts, _topK)))
            .ToList();

        var edges = new List<RagConceptEdgeDraft>(_cooccurrence.Build(forCooccurrence, _minEdgeWeight));
        foreach (var chunk in chunks)
            edges.AddRange(RagLegalReferenceExtractor.BuildEdges(chunk.ArticleMain, chunk.Text));

        return (capped, edges);
    }

    /// <summary>全 source から辺を読み直し、コミュニティ（2 階層）を作り直す。</summary>
    public void RebuildCommunities(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        var concepts = _graph.LoadConcepts(conn);
        var edges = _graph.LoadAggregatedEdges(conn)
            .GroupBy(e => (e.A, e.B))
            .Select(g => (g.Key.A, g.Key.B, g.Sum(e => e.Weight)))
            .ToList();
        var headers = _graph.LoadTopHeaderPerConcept(conn);

        var nodes = concepts.Select(c => new RagCommunityDetector.Node(c.Id, c.Term)).ToList();
        var communities = RagCommunityDetector.Detect(nodes, edges, headers);
        _graph.ReplaceCommunities(conn, communities, tx);
    }

    /// <summary>全 source を作り直す（起動時バックフィルと設定画面の再構築）。</summary>
    public void RebuildAll(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var source in LoadSources(conn))
                IndexSource(conn, source, tx);
            RebuildCommunities(conn, tx);
            _graph.SetMeta(conn, RagSqliteGraph.BuiltVersionKey, RagSqliteGraph.BuildVersion, tx);
            _graph.SetMeta(conn, RagSqliteGraph.BuiltAtKey, DateTime.UtcNow.ToString("O"), tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>source 削除後の後始末（概念の掃除とコミュニティ再計算）。辺・チャンク概念は RagSqliteGraph.DeleteSource で消す。</summary>
    public void AfterSourceRemoved(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        _graph.PruneUnusedConcepts(conn, tx);
        RebuildCommunities(conn, tx);
    }

    public void MarkBuilt(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        _graph.SetMeta(conn, RagSqliteGraph.BuiltVersionKey, RagSqliteGraph.BuildVersion, tx);
        _graph.SetMeta(conn, RagSqliteGraph.BuiltAtKey, DateTime.UtcNow.ToString("O"), tx);
    }

    public static IReadOnlyList<RagGraphChunkInput> LoadChunks(SqliteConnection conn, string source)
    {
        var list = new List<RagGraphChunkInput>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source, header_text, text, entry_key, article_main, doc_kind
            FROM rag_chunks WHERE source = $s ORDER BY id
            """;
        cmd.Parameters.AddWithValue("$s", source);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RagGraphChunkInput(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6)));
        }

        return list;
    }

    private static IReadOnlyList<string> LoadSources(SqliteConnection conn)
    {
        var list = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT source FROM rag_chunks ORDER BY source";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }
}
