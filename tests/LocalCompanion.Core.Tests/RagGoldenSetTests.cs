using LocalCompanion.Core.Tests.Fixtures;
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

    private static void AssertSkip(RagGoldenCase c)
    {
        var plan = RagQueryPlanner.Plan(c.Query, null);
        Assert.Equal(RagConversationMode.Skip, RagConversationGate.Resolve(plan, c.Query));
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
