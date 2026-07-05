using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagQueryPlannerFaqTests
{
    [Fact]
    public void Plan_FaqIntent_ForProcedureQuestion()
    {
        var plan = RagQueryPlanner.Plan("有給休暇の取得方法は？", null);
        Assert.Equal(RagQueryIntent.Faq, plan.Intent);
        Assert.Equal(RagResponseMode.Verbatim, plan.ResponseMode);
        Assert.False(string.IsNullOrWhiteSpace(plan.TopicKeyword));
    }
}
