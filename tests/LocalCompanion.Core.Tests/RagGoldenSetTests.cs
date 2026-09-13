using LocalCompanion.Core.Tests.Fixtures;
using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests;

/// <summary>
/// RAG 黄金セット（約30問）。llama なしでプラン／ゲート／構造化検索／Soft 床を回帰する。
/// </summary>
public sealed class RagGoldenSetTests : IDisposable
{
    private readonly SqliteConnection _conn = RagGoldenCorpus.OpenFilled();

    public void Dispose() => _conn.Dispose();

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in RagGoldenCases.All)
            data.Add(c.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void GoldenCase_Passes(string id)
    {
        var c = RagGoldenCases.All.First(x => x.Id == id);
        switch (c.Kind)
        {
            case RagGoldenKind.Skip:
                AssertSkip(c);
                break;
            case RagGoldenKind.Mode:
                AssertMode(c);
                break;
            case RagGoldenKind.ArticleHit:
                AssertArticleHit(c);
                break;
            case RagGoldenKind.ArticleMiss:
                AssertArticleMiss(c);
                break;
            case RagGoldenKind.BoundaryHit:
                AssertBoundaryHit(c);
                break;
            case RagGoldenKind.DefinitionHit:
                AssertDefinitionHit(c);
                break;
            case RagGoldenKind.SoftPolicy:
                AssertSoftPolicy(c);
                break;
            case RagGoldenKind.RiskPolicy:
                AssertRiskPolicy(c);
                break;
            case RagGoldenKind.Verbatim:
                AssertVerbatim(c);
                break;
            case RagGoldenKind.PromptCopy:
                AssertPromptCopy(c);
                break;
            case RagGoldenKind.EvidenceAttach:
                AssertEvidenceAttach(c);
                break;
            case RagGoldenKind.Overview:
                AssertOverview(c);
                break;
            case RagGoldenKind.MultiHop:
                AssertMultiHop(c);
                break;
            default:
                Assert.Fail($"Unknown kind {c.Kind}");
                break;
        }
    }

    [Fact]
    public void GoldenSet_HasAtLeastThirtyCases()
    {
        Assert.True(RagGoldenCases.All.Count >= 30, $"got {RagGoldenCases.All.Count}");
        Assert.Equal(RagGoldenCases.All.Count, RagGoldenCases.All.Select(c => c.Id).Distinct().Count());
    }

    /// <summary>評価セット（未実装分）は ID が重複せず、実装済みの All とも被らない。</summary>
    [Fact]
    public void PlannedSet_IdsAreUniqueAndNotInAll()
    {
        var planned = RagGoldenCases.Planned;
        Assert.Equal(planned.Count, planned.Select(c => c.Id).Distinct().Count());
        var live = RagGoldenCases.All.Select(c => c.Id).ToHashSet();
        Assert.DoesNotContain(planned, c => live.Contains(c.Id));
    }

    /// <summary>評価用コーパスの新資料（README・窃盗抜粋）が構造チャンクとして入っている。</summary>
    [Fact]
    public void Corpus_ContainsReadmeAndTheftSources()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT source, COUNT(*), MAX(doc_kind) FROM rag_chunks WHERE source IN ($a, $b) GROUP BY source";
        cmd.Parameters.AddWithValue("$a", RagGoldenCorpus.ReadmeSource);
        cmd.Parameters.AddWithValue("$b", RagGoldenCorpus.TheftSource);
        var kinds = new Dictionary<string, (int Count, string Kind)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            kinds[reader.GetString(0)] = (reader.GetInt32(1), reader.GetString(2));

        Assert.True(kinds[RagGoldenCorpus.ReadmeSource].Count >= 3, $"readme chunks: {kinds[RagGoldenCorpus.ReadmeSource].Count} / {DescribeHeaders(RagGoldenCorpus.ReadmeSource)}");
        Assert.Equal("general", kinds[RagGoldenCorpus.ReadmeSource].Kind);
        Assert.True(kinds[RagGoldenCorpus.TheftSource].Count >= 4, $"theft chunks: {kinds[RagGoldenCorpus.TheftSource].Count} / {DescribeHeaders(RagGoldenCorpus.TheftSource)}");
        Assert.Equal("legal", kinds[RagGoldenCorpus.TheftSource].Kind);
    }

    private string DescribeHeaders(string source)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT header_text, length(text) FROM rag_chunks WHERE source = $s ORDER BY id";
        cmd.Parameters.AddWithValue("$s", source);
        var parts = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            parts.Add($"{reader.GetString(0)}({reader.GetInt64(1)})");
        return string.Join(", ", parts);
    }

    /// <summary>何も載らない: 門番で Skip、または Probe の語彙検索に質問の語が実在しない。</summary>
    private void AssertSkip(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        var mode = RagConversationGate.Resolve(plan, c.Query);
        Assert.True(mode is RagConversationMode.Skip or RagConversationMode.Probe, $"mode was {mode}");
        if (mode == RagConversationMode.Skip)
            return;

        var hits = RagGoldenCorpus.SearchFts(_conn, c.Query);
        var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(hits, plan), c.Query, mode);
        Assert.Empty(filtered.Hits);
    }

    /// <summary>合図語なしでも、語彙検索で根拠のある資料が載る（Probe）。合図語ありなら SoftTopic でもよい。</summary>
    private void AssertEvidenceAttach(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        var mode = RagConversationGate.Resolve(plan, c.Query);
        Assert.True(mode is RagConversationMode.Probe or RagConversationMode.SoftTopic, $"mode was {mode}");

        var hits = RagGoldenCorpus.SearchFts(_conn, c.Query);
        var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(hits, plan), c.Query, mode);
        Assert.NotEmpty(filtered.Hits);
        Assert.True(filtered.Hits.Count <= RagEvidenceGate.MaxProbeHits);
        if (c.ExpectSourceContains is not null)
            Assert.Contains(c.ExpectSourceContains, filtered.Hits[0].Source, StringComparison.OrdinalIgnoreCase);
        if (c.ExpectHeaderContains is not null)
            Assert.Contains(c.ExpectHeaderContains, filtered.Hits[0].HeaderText + filtered.Hits[0].Text, StringComparison.Ordinal);
    }

    /// <summary>俯瞰: Overview 意図になり、グラフ検索（LLM は Fake）が資料から材料を返す。</summary>
    private void AssertOverview(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagQueryIntent.Overview, plan.Intent);
        if (c.ExpectSourceContains is null)
            return;

        var graph = RagGoldenCorpus.BuildGraph(_conn);
        var fts = new RagSqliteFts();
        fts.TryPrepare(_conn);
        var llm = FakeGraphLlm.JudgeByWords(c.Query);
        var search = new RagGraphSearch(graph, fts, llm);
        var hits = search.SearchAsync(_conn, c.Query, RagGoldenCorpus.AllSources, 5, RagGraphSearchBudget.Default, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotEmpty(hits);
        Assert.Contains(c.ExpectSourceContains, hits[0].Source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>多段: FTS＋グラフ第 3 レーンの融合で、質問語を含まない参照先の条にも届く。</summary>
    private void AssertMultiHop(RagGoldenCase c)
    {
        var graph = RagGoldenCorpus.BuildGraph(_conn);
        var hits = RagGoldenCorpus.SearchFtsWithGraphLane(_conn, graph, c.Query);

        Assert.NotEmpty(hits);
        var match = hits.FirstOrDefault(h => h.HeaderText.Contains(c.ExpectHeaderContains!, StringComparison.Ordinal));
        Assert.True(match is not null, $"expected {c.ExpectHeaderContains} in: {string.Join(" | ", hits.Select(h => h.HeaderText))}");
        Assert.Contains(c.ExpectSourceContains!, match!.Source, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertMode(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        var mode = RagConversationGate.Resolve(plan, c.Query);
        Assert.Equal(c.ExpectMode, mode.ToString());
    }

    private void AssertArticleHit(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        if (c.ExpectArticleSortKey is long key)
            Assert.Equal(key, plan.ArticleSortKey);

        var hits = SearchStructured(plan);
        hits = RagArticleHitFilter.KeepMatching(plan, hits);
        Assert.False(RagArticleHitFilter.IsMiss(plan, hits));
        Assert.NotEmpty(hits);
        if (c.ExpectSourceContains is not null)
            Assert.Contains(c.ExpectSourceContains, hits[0].Source, StringComparison.OrdinalIgnoreCase);
        if (c.ExpectHeaderContains is not null)
            Assert.Contains(c.ExpectHeaderContains, hits[0].HeaderText + hits[0].Text, StringComparison.Ordinal);
    }

    private void AssertArticleMiss(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagQueryIntent.Article, plan.Intent);
        var hits = SearchStructured(plan);
        hits = RagArticleHitFilter.KeepMatching(plan, hits);
        Assert.True(RagArticleHitFilter.IsMiss(plan, hits));
        Assert.True(c.ExpectMiss);
    }

    private void AssertBoundaryHit(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagQueryIntent.Boundary, plan.Intent);
        var hits = SearchStructured(plan);
        Assert.NotEmpty(hits);
        if (c.ExpectSourceContains is not null)
            Assert.Contains(c.ExpectSourceContains, hits[0].Source, StringComparison.OrdinalIgnoreCase);
        if (c.ExpectArticleSortKey is long key)
            Assert.Equal(key, hits[0].ArticleSortKey);
    }

    private void AssertDefinitionHit(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagQueryIntent.Definition, plan.Intent);
        var hits = SearchStructured(plan);
        Assert.NotEmpty(hits);
        if (c.ExpectSourceContains is not null)
            Assert.Contains(c.ExpectSourceContains, hits[0].Source, StringComparison.OrdinalIgnoreCase);
        if (c.ExpectReplyContains is not null)
            Assert.Contains(c.ExpectReplyContains, hits[0].Text, StringComparison.Ordinal);
    }

    private static void AssertSoftPolicy(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagConversationMode.SoftTopic, RagConversationGate.Resolve(plan, c.Query));

        if (c.ExpectSoftEmpty)
        {
            var noise = new[]
            {
                new RagSearchHit("AIさんは女の子ですかメモ", RagGoldenCorpus.NotesSource, "雑談", 1, "n1"),
            };
            var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(noise, plan), c.Query, RagConversationMode.SoftTopic);
            Assert.Empty(filtered.Hits);
            return;
        }

        if (c.ExpectSoftKeepsLegal)
        {
            var hits = new[]
            {
                new RagSearchHit("AI雑談", RagGoldenCorpus.NotesSource, "雑談", 1, "n1"),
                new RagSearchHit("残業と四十時間の上限について法律のメモ", RagGoldenCorpus.LaborSource, "労働時間", 1, "l1"),
            };
            var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(hits, plan), c.Query, RagConversationMode.SoftTopic);
            Assert.NotEmpty(filtered.Hits);
            Assert.Contains(filtered.Hits, h => h.Source.Contains("労働", StringComparison.Ordinal));
            return;
        }

        if (c.ExpectRankFirstSourceContains)
        {
            var hits = new[]
            {
                new RagSearchHit("雑談メモ本文", RagGoldenCorpus.NotesSource, "雑談", 1, "n1"),
                new RagSearchHit("使用者は残業させてはならない。一週間について四十時間を超えて労働させてはならない。", RagGoldenCorpus.LaborSource, "第32条", 1, "l32"),
                new RagSearchHit("四十時間と残業の解説メモ", "解説メモ.md", "解説", 1, "x1"),
            };
            var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(hits, plan), c.Query, RagConversationMode.SoftTopic);
            Assert.NotEmpty(filtered.Hits);
            Assert.Contains("労働基準法", filtered.Hits[0].Source, StringComparison.Ordinal);
            Assert.True(filtered.Hits.Count <= RagSoftHitRanker.DefaultTake);
        }
    }

    private static void AssertRiskPolicy(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagConversationMode.RiskCaution, RagConversationGate.Resolve(plan, c.Query));
        var hits = new[]
        {
            new RagSearchHit("雑談メモ", RagGoldenCorpus.NotesSource, "メモ", 1, "n1"),
            new RagSearchHit("他人の財物を窃取した者は、窃盗の罪とする。", RagGoldenCorpus.PenalSource, "第235条", 1, "c235"),
        };
        var filtered = RagConversationGate.ApplyHitPolicy(new RagSearchResult(hits, plan), c.Query, RagConversationMode.RiskCaution);
        Assert.NotEmpty(filtered.Hits);
        Assert.All(filtered.Hits, h => Assert.Contains("刑法", h.Source, StringComparison.Ordinal));
    }

    private void AssertVerbatim(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        var hits = SearchStructured(plan);
        hits = RagArticleHitFilter.KeepMatching(plan, hits);

        if (c.ExpectMiss)
        {
            Assert.True(RagArticleHitFilter.IsMiss(plan, hits));
            var reply = RagVerbatimGuard.BuildMissReply(plan, japanese: true);
            Assert.Contains(c.ExpectReplyContains!, reply, StringComparison.Ordinal);
            return;
        }

        Assert.True(RagVerbatimResponder.TryFormat(plan, hits, japanese: true, out var ok));
        Assert.Contains(c.ExpectReplyContains!, ok, StringComparison.Ordinal);
    }

    private static void AssertPromptCopy(RagGoldenCase c)
    {
        var text = ChatSystemPromptTexts.RagEmptyHitsInstruction(japanese: true, searchFailed: c.SearchFailedPrompt);
        Assert.Contains(c.ExpectReplyContains!, text, StringComparison.Ordinal);
    }

    private IReadOnlyList<RagSearchHit> SearchStructured(RagQueryPlan plan)
    {
        var sources = RagGoldenCorpus.AllSources;
        if (!string.IsNullOrWhiteSpace(plan.SourceHint))
        {
            var filtered = sources
                .Where(s => RagSourceHintResolver.MatchesHint(s, plan.SourceHint!))
                .ToList();
            if (filtered.Count > 0)
                sources = filtered;
        }

        return RagStructuredSearch.Execute(_conn, plan, sources, topK: 5);
    }
}
