using LocalCompanion.Data;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Services;

/// <summary>
/// 一般質問の検索に足す「グラフの第 3 レーン」。
/// (1) 質問の概念の隣接語で FTS を広げる、(2) 上位ヒットと同じコミュニティのチャンクを RRF に加点する。
/// グラフが空なら何もしない（従来どおり FTS＋ベクトル）。
/// </summary>
internal sealed class RagGraphLane
{
    public const int DefaultNeighborTerms = 5;
    public const int DefaultSeedHits = 5;

    private readonly RagSqliteGraph _graph;
    private readonly IRagWordSegmenter _segmenter;

    public RagGraphLane(RagSqliteGraph graph, IRagWordSegmenter segmenter)
    {
        _graph = graph;
        _segmenter = segmenter;
    }

    /// <summary>質問の語の隣接概念（表示語）。FTS の OR 句に足す用。</summary>
    public IReadOnlyList<string> ExpandQueryTerms(SqliteConnection conn, string query, int top = DefaultNeighborTerms)
    {
        var norms = QueryConceptNorms(query);
        if (norms.Count == 0)
            return Array.Empty<string>();

        var ids = _graph.FindConceptIds(conn, norms);
        if (ids.Count == 0)
            return Array.Empty<string>();

        return _graph.TopNeighbors(conn, ids.Values.ToList(), top)
            .Select(n => n.Term)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    /// <summary>隣接語を索引と同じ割り方のフレーズにして、既存の MATCH 句に OR で継ぎ足す。</summary>
    public static string AppendToMatchQuery(string matchQuery, IReadOnlyList<string> neighborTerms)
    {
        var phrases = neighborTerms
            .Select(RagFtsSegmenter.Segment)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => $"\"{s}\"")
            .ToList();
        if (phrases.Count == 0)
            return matchQuery;

        var extra = string.Join(" OR ", phrases);
        return string.IsNullOrWhiteSpace(matchQuery) ? extra : matchQuery + " OR " + extra;
    }

    /// <summary>上位ヒットと同じ level-0 コミュニティのチャンク id（概念の一致数順）。</summary>
    public IReadOnlyList<long> SameCommunityChunkIds(
        SqliteConnection conn,
        IReadOnlyList<long> seedChunkIds,
        IReadOnlyCollection<string> sources,
        int limit,
        int seedHits = DefaultSeedHits)
    {
        if (seedChunkIds.Count == 0 || limit <= 0)
            return Array.Empty<long>();

        var seeds = seedChunkIds.Take(seedHits).ToList();
        var concepts = _graph.ConceptIdsForChunks(conn, seeds);
        if (concepts.Count == 0)
            return Array.Empty<long>();

        var communities = _graph.CommunityIdsForConcepts(conn, concepts, level: 0);
        if (communities.Count == 0)
            return Array.Empty<long>();

        return _graph.RankChunksByCommunities(conn, communities, 0, sources, limit);
    }

    private List<string> QueryConceptNorms(string query)
    {
        var norms = new List<string>();
        foreach (var word in _segmenter.Segment(query))
        {
            var norm = RagConceptExtractor.Normalize(word);
            if (norm.Length < 2 || RagConceptStopwords.IsStopword(norm) || norms.Contains(norm))
                continue;
            norms.Add(norm);
        }

        return norms;
    }
}
