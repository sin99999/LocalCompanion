using LocalCompanion.Core.Tests.Fixtures;
using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>俯瞰検索: LLM は Fake。予算・退化版・要点圧縮の分岐を固定する。</summary>
public sealed class RagGraphSearchTests : IDisposable
{
    private const string Query = "窃盗と強盗の違いを比較して";

    private readonly SqliteConnection _conn = RagGoldenCorpus.OpenFilled();
    private readonly RagSqliteGraph _graph;
    private readonly RagSqliteFts _fts = new();

    public RagGraphSearchTests()
    {
        _graph = RagGoldenCorpus.BuildGraph(_conn);
        _fts.TryPrepare(_conn);
    }

    public void Dispose() => _conn.Dispose();

    private Task<IReadOnlyList<RagSearchHit>> SearchAsync(FakeGraphLlm llm, RagGraphSearchBudget? budget = null, string query = Query) =>
        new RagGraphSearch(_graph, _fts, llm).SearchAsync(_conn, query, RagGoldenCorpus.AllSources, 5, budget ?? RagGraphSearchBudget.Default, CancellationToken.None);

    [Fact]
    public async Task Search_FullPath_ReturnsCondensedGraphHitFromTheftSource()
    {
        var llm = FakeGraphLlm.JudgeByWords(Query, mapReply: "- 窃盗は十年以下の懲役又は罰金\n- 強盗は五年以上の有期懲役");

        var hits = await SearchAsync(llm);

        Assert.NotEmpty(hits);
        var graphHits = hits.Where(h => h.ChunkId == RagGraphSearch.GraphChunkId).ToList();
        Assert.NotEmpty(graphHits);
        Assert.True(graphHits.Count <= 3, $"graph hits: {graphHits.Count}");
        Assert.Contains("窃盗", graphHits[0].Text);
        Assert.Contains("窃盗", graphHits[0].Source);
        Assert.All(graphHits, h => Assert.False(string.IsNullOrWhiteSpace(h.HeaderText)));
        Assert.Equal(1, llm.RefineCalls);
        Assert.InRange(llm.RelevanceCalls, 1, RagGraphSearchBudget.Default.RelevanceCalls);
        Assert.True(llm.MapCalls >= 1);
    }

    [Fact]
    public async Task Search_LlmDead_FallsBackToCommunityRepresentatives()
    {
        var hits = await SearchAsync(FakeGraphLlm.Dead);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.NotEqual(RagGraphSearch.GraphChunkId, h.ChunkId));
        Assert.Equal(hits.Count, hits.Select(h => h.Source + "\0" + h.HeaderText).Distinct().Count());
    }

    [Fact]
    public async Task Search_ZeroRelevanceBudget_MakesNoLlmCalls_StillReturnsRepresentatives()
    {
        var llm = FakeGraphLlm.JudgeByWords(Query);

        var hits = await SearchAsync(llm, new RagGraphSearchBudget(0, TimeSpan.FromSeconds(5)));

        Assert.NotEmpty(hits);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task Search_RelevanceBudget_CapsJudgeCalls()
    {
        var llm = FakeGraphLlm.JudgeByWords(Query);

        await SearchAsync(llm, new RagGraphSearchBudget(2, TimeSpan.FromSeconds(5)));

        Assert.True(llm.RelevanceCalls <= 2, $"relevance calls: {llm.RelevanceCalls}");
    }

    [Fact]
    public async Task Search_MapSaysNoContent_ReturnsRawChunksInstead()
    {
        var llm = FakeGraphLlm.JudgeByWords(Query, mapReply: "該当なし");

        var hits = await SearchAsync(llm);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.NotEqual(RagGraphSearch.GraphChunkId, h.ChunkId));
    }

    [Fact]
    public async Task Search_AllJudgedIrrelevant_ReturnsRepresentativesNotEmpty()
    {
        var llm = new FakeGraphLlm((system, _) => ReferenceEquals(system, RagGraphPrompts.RelevanceSystem) ? "0" : null);

        var hits = await SearchAsync(llm);

        Assert.NotEmpty(hits);
        Assert.Equal(0, llm.MapCalls);
    }

    [Fact]
    public async Task Search_NoLexicalCandidates_ReturnsEmpty()
    {
        var llm = FakeGraphLlm.JudgeByWords("クロワッサン");

        var hits = await SearchAsync(llm, query: "クロワッサンの焼き方を比較して");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_EmptyGraph_ReturnsEmptyWithoutLlm()
    {
        using var bare = RagGoldenCorpus.OpenFilled();
        var graph = new RagSqliteGraph();
        graph.EnsureTables(bare);
        var fts = new RagSqliteFts();
        fts.TryPrepare(bare);
        var llm = FakeGraphLlm.JudgeByWords(Query);

        var hits = await new RagGraphSearch(graph, fts, llm).SearchAsync(bare, Query, RagGoldenCorpus.AllSources, 5, RagGraphSearchBudget.Default, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Equal(0, llm.Calls);
    }
}
