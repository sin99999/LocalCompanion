using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagSourceHintResolverTests
{
    [Fact]
    public void ResolvePrimary_MatchesRegisteredFilenameToken()
    {
        var sources = new[] { @"C:\docs\cpp-reference.md", @"C:\law\刑法.md" };
        var hint = RagSourceHintResolver.ResolvePrimary("cppのvectorとは", sources);
        Assert.Equal("cpp", hint);
    }

    [Fact]
    public void ResolvePrimary_MatchesRegisteredUrl()
    {
        var sources = new[] { "url:https://en.cppreference.com/w/cpp/container/vector" };
        var hint = RagSourceHintResolver.ResolvePrimary("cppreferenceのvector", sources);
        Assert.NotNull(hint);
        Assert.Contains("cpp", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnrichPlan_AddsDynamicHint()
    {
        var plan = RagQueryPlanner.Plan("rubyのeachの使い方", previousUserMessage: null);
        var sources = new[] { @"D:\notes\ruby-collections.md" };
        var enriched = RagSourceHintResolver.EnrichPlan(plan, sources);
        Assert.Equal("ruby", enriched.SourceHint);
    }
}
