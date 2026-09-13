using System.Diagnostics;
using LocalCompanion.Data;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Services;

/// <summary>俯瞰検索の予算（関連判定の回数と時間）。切れたらそこまでの結果で返す。</summary>
public sealed record RagGraphSearchBudget(int RelevanceCalls, TimeSpan Time)
{
    public static RagGraphSearchBudget Default { get; } = new(12, TimeSpan.FromSeconds(8));
}

/// <summary>
/// 俯瞰質問（Overview）の検索。LazyGraphRAG 風に、索引は LLM なし、質問時だけ短い LLM を予算内で使う。
/// 1. 質問を 2〜3 個の検索語に分ける（LLM 1 回、失敗なら元の質問）
/// 2. FTS でチャンク候補を集め、概念グラフのコミュニティ単位に束ねて票数順に並べる
/// 3. コミュニティごとに候補チャンクの関連判定（0/1、予算内。空振り 2 連続で打ち切り）
/// 4. 関連ありのコミュニティごとに要点を LLM で 3 行に圧縮して 1 ヒットにする（失敗ならチャンクそのまま）
/// LLM が死んでいるときはコミュニティ代表チャンクだけ返す（退化版）。
/// </summary>
internal sealed class RagGraphSearch
{
    public const string GraphChunkId = "__graph__";
    private const int CandidatesPerSubquery = 30;
    private const int ChunksJudgedPerCommunity = 3;
    private const int MaxMappedCommunities = 3;
    private const int NoCommunity = -1;

    private readonly RagSqliteGraph _graph;
    private readonly RagSqliteFts _fts;
    private readonly IRagGraphLlm _llm;
    private readonly Func<SqliteConnection, IReadOnlyList<long>, IReadOnlyDictionary<long, RagSearchHit>> _loadHits;

    public RagGraphSearch(
        RagSqliteGraph graph,
        RagSqliteFts fts,
        IRagGraphLlm llm,
        Func<SqliteConnection, IReadOnlyList<long>, IReadOnlyDictionary<long, RagSearchHit>>? loadHits = null)
    {
        _graph = graph;
        _fts = fts;
        _llm = llm;
        _loadHits = loadHits ?? RagService.LoadHitsKeyedById;
    }

    public async Task<IReadOnlyList<RagSearchHit>> SearchAsync(
        SqliteConnection conn,
        string query,
        IReadOnlyList<string> sources,
        int topK,
        RagGraphSearchBudget budget,
        CancellationToken ct)
    {
        if (topK <= 0 || sources.Count == 0 || !_fts.IsAvailable || _graph.GetStats(conn).ConceptCount == 0)
            return Array.Empty<RagSearchHit>();

        var clock = Stopwatch.StartNew();
        var subqueries = await RefineAsync(query, budget, clock, ct);
        var candidates = CollectCandidates(conn, subqueries, sources);
        if (candidates.Count == 0)
            return Array.Empty<RagSearchHit>();

        var communities = GroupByCommunity(conn, candidates);
        var hitsById = _loadHits(conn, candidates.Keys.OrderBy(id => id).ToList());

        var (relevant, llmAlive) = await JudgeAsync(query, communities, hitsById, budget, clock, ct);
        if (!llmAlive || relevant.Count == 0)
            return Representatives(communities, hitsById, topK);

        var labels = _graph.LoadCommunityLabels(conn, relevant.Select(r => r.CommunityId).Where(id => id != NoCommunity).Distinct().ToList());
        return await MapAsync(query, relevant, labels, topK, budget, clock, ct);
    }

    // --- 1. Refine ---

    private async Task<IReadOnlyList<string>> RefineAsync(string query, RagGraphSearchBudget budget, Stopwatch clock, CancellationToken ct)
    {
        var fallback = new[] { query };
        if (budget.RelevanceCalls <= 0 || clock.Elapsed > budget.Time)
            return fallback;

        var reply = await _llm.CompleteAsync(
            RagGraphPrompts.RefineSystem,
            RagGraphPrompts.RefineUser(query),
            maxTokens: 80,
            ct);
        var lines = RagGraphPrompts.ParseLines(reply);
        if (lines.Count == 0)
            return fallback;

        return lines.Take(3).Append(query).Distinct(StringComparer.Ordinal).ToList();
    }

    // --- 2. Match ---

    private Dictionary<long, int> CollectCandidates(SqliteConnection conn, IReadOnlyList<string> subqueries, IReadOnlyList<string> sources)
    {
        var bestRank = new Dictionary<long, int>();
        foreach (var sub in subqueries)
        {
            var match = RagSqliteFts.BuildSegmentedMatchQuery(sub);
            if (string.IsNullOrWhiteSpace(match))
                continue;

            var ids = _fts.Search(conn, match, CandidatesPerSubquery, sources);
            for (var i = 0; i < ids.Count; i++)
                bestRank[ids[i]] = Math.Min(bestRank.GetValueOrDefault(ids[i], int.MaxValue), i);
        }

        return bestRank;
    }

    private sealed record CommunityBucket(long Id, IReadOnlyList<long> ChunkIds, int Votes);

    private List<CommunityBucket> GroupByCommunity(SqliteConnection conn, Dictionary<long, int> candidates)
    {
        var pairs = _graph.CommunitiesForChunks(conn, candidates.Keys.ToList(), level: 0);
        var buckets = pairs
            .GroupBy(p => p.CommunityId)
            .Select(g => new CommunityBucket(
                g.Key,
                g.Select(p => p.ChunkId).Distinct().OrderBy(c => candidates[c]).ThenBy(c => c).ToList(),
                g.Select(p => p.ChunkId).Distinct().Count()))
            .OrderByDescending(b => b.Votes)
            .ThenBy(b => b.ChunkIds.Min(c => candidates[c]))
            .ThenBy(b => b.Id)
            .ToList();

        var assigned = new HashSet<long>(pairs.Select(p => p.ChunkId));
        var loose = candidates.Keys.Where(id => !assigned.Contains(id)).OrderBy(id => candidates[id]).ThenBy(id => id).ToList();
        if (loose.Count > 0)
            buckets.Add(new CommunityBucket(NoCommunity, loose, loose.Count));

        return buckets;
    }

    // --- 3. Relevance test (iterative deepening within budget) ---

    private sealed record RelevantChunk(long CommunityId, long ChunkId, RagSearchHit Hit);

    private async Task<(List<RelevantChunk> Relevant, bool LlmAlive)> JudgeAsync(
        string query,
        List<CommunityBucket> communities,
        IReadOnlyDictionary<long, RagSearchHit> hitsById,
        RagGraphSearchBudget budget,
        Stopwatch clock,
        CancellationToken ct)
    {
        var relevant = new List<RelevantChunk>();
        var calls = 0;
        var emptyStreak = 0;
        foreach (var community in communities)
        {
            var found = 0;
            foreach (var chunkId in community.ChunkIds.Take(ChunksJudgedPerCommunity))
            {
                if (calls >= budget.RelevanceCalls || clock.Elapsed > budget.Time)
                    return (relevant, true);
                if (!hitsById.TryGetValue(chunkId, out var hit))
                    continue;

                var verdict = await _llm.CompleteAsync(
                    RagGraphPrompts.RelevanceSystem,
                    RagGraphPrompts.RelevanceUser(query, hit),
                    maxTokens: 3,
                    ct);
                calls++;
                if (verdict is null)
                    return (relevant, false);
                if (!RagGraphPrompts.ParseYes(verdict))
                    continue;

                relevant.Add(new RelevantChunk(community.Id, chunkId, hit));
                found++;
            }

            emptyStreak = found == 0 ? emptyStreak + 1 : 0;
            if (emptyStreak >= 2)
                break;
        }

        return (relevant, true);
    }

    // --- 4. Map (community → one condensed hit) ---

    private async Task<IReadOnlyList<RagSearchHit>> MapAsync(
        string query,
        List<RelevantChunk> relevant,
        IReadOnlyDictionary<long, string> labels,
        int topK,
        RagGraphSearchBudget budget,
        Stopwatch clock,
        CancellationToken ct)
    {
        var hits = new List<RagSearchHit>();
        var groups = relevant
            .GroupBy(r => r.CommunityId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(MaxMappedCommunities);

        foreach (var group in groups)
        {
            var chunks = group.Select(r => r.Hit).ToList();
            var label = labels.GetValueOrDefault(group.Key, "");
            var header = string.IsNullOrWhiteSpace(label) ? chunks[0].HeaderText : label;
            var sectionPath = string.Join(" / ", chunks.Select(c => c.HeaderText).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct());

            string? summary = null;
            if (clock.Elapsed <= budget.Time)
            {
                summary = await _llm.CompleteAsync(
                    RagGraphPrompts.MapSystem,
                    RagGraphPrompts.MapUser(query, chunks),
                    maxTokens: 220,
                    ct);
            }

            if (string.IsNullOrWhiteSpace(summary) || RagGraphPrompts.IsNoContent(summary))
            {
                hits.AddRange(chunks);
                continue;
            }

            hits.Add(new RagSearchHit(
                summary.Trim(),
                chunks[0].Source,
                header,
                chunks[0].Page,
                GraphChunkId,
                SectionPath: sectionPath));
        }

        return hits.Take(Math.Max(topK, MaxMappedCommunities)).ToList();
    }

    /// <summary>退化版: 票数順のコミュニティから 1 チャンクずつ。</summary>
    private static IReadOnlyList<RagSearchHit> Representatives(
        List<CommunityBucket> communities,
        IReadOnlyDictionary<long, RagSearchHit> hitsById,
        int topK)
    {
        var hits = new List<RagSearchHit>();
        var seen = new HashSet<long>();
        foreach (var community in communities)
        {
            foreach (var chunkId in community.ChunkIds)
            {
                if (!seen.Add(chunkId) || !hitsById.TryGetValue(chunkId, out var hit))
                    continue;
                hits.Add(hit);
                break;
            }

            if (hits.Count >= topK)
                break;
        }

        return hits;
    }
}
