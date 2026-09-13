using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>条文の参照（範囲・前条・準用）を決定論的に解く。</summary>
public sealed class RagLegalReferenceExtractorTests
{
    [Fact]
    public void Range_ExpandsToEveryArticleBetween()
    {
        var refs = RagLegalReferenceExtractor.ExtractReferencedArticles(243, "第235条から第236条までの罪の未遂は、罰する。");

        Assert.Equal([235, 236], refs);
    }

    [Fact]
    public void RelativeReferences_ResolveAgainstOwnArticle()
    {
        Assert.Equal([243], RagLegalReferenceExtractor.ExtractReferencedArticles(244, "前条の規定は、配偶者との間で犯した罪について準用する。"));
        Assert.Equal([242, 243], RagLegalReferenceExtractor.ExtractReferencedArticles(244, "前二条の罪は、告訴がなければ公訴を提起することができない。"));
        Assert.Equal([245], RagLegalReferenceExtractor.ExtractReferencedArticles(244, "次条の規定を準用する。"));
    }

    [Fact]
    public void ExplicitArticles_CollectedSortedWithoutSelf()
    {
        var refs = RagLegalReferenceExtractor.ExtractReferencedArticles(251, "第244条の規定は、第235条の罪及び第251条について準用する。");

        Assert.Equal([235, 244], refs);
    }

    [Fact]
    public void Range_TooWide_IsNotExpanded()
    {
        var refs = RagLegalReferenceExtractor.ExtractReferencedArticles(5, "第1条から第100条までの規定");

        Assert.Contains(1, refs);
        Assert.Contains(100, refs);
        Assert.DoesNotContain(50, refs);
    }

    [Fact]
    public void BuildEdges_UsesRefersKindAndNormalizedArticleTerms()
    {
        var edges = RagLegalReferenceExtractor.BuildEdges(243, "第235条から第236条までの罪の未遂は、罰する。");

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(RagConceptEdgeDraft.KindRefers, e.Kind));
        Assert.All(edges, e => Assert.Equal("第243条", e.NormA));
        Assert.Equal(["第235条", "第236条"], edges.Select(e => e.NormB));
        Assert.Empty(RagLegalReferenceExtractor.BuildEdges(0, "第235条"));
    }
}
