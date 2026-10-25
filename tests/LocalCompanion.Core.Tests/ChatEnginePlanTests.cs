using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatEnginePlanTests
{
    [Fact]
    public void Decide_Default_StartsLlamaAndDoesNotStopIt()
    {
        var plan = ChatEnginePlan.Decide(null, strataCommandExists: true, allowModelDownload: true);

        Assert.Equal(InferenceEngineChoices.LlamaCpp, plan.Engine);
        Assert.True(plan.StartLlama);
        Assert.False(plan.StopManagedLlama);
        Assert.False(plan.StartStrata);
        Assert.Empty(plan.StrataArguments);
    }

    [Fact]
    public void Decide_StrataBeforeDownload_KeepsLlamaRunning()
    {
        var plan = ChatEnginePlan.Decide(
            InferenceEngineChoices.Strata,
            strataCommandExists: true,
            allowModelDownload: false);

        Assert.True(plan.HoldStrataUntilDownload);
        Assert.True(plan.StartLlama);
        Assert.False(plan.StopManagedLlama);
        Assert.False(plan.StartStrata);
    }

    [Fact]
    public void Decide_StrataWithoutCommand_KeepsLlamaRunning()
    {
        var plan = ChatEnginePlan.Decide(
            "STRATA",
            strataCommandExists: false,
            allowModelDownload: true);

        Assert.True(plan.StartLlama);
        Assert.False(plan.StartStrata);
    }

    [Fact]
    public void Decide_StrataReady_StopsOnlyManagedLlamaAndStaysOnLoopback()
    {
        var plan = ChatEnginePlan.Decide(
            InferenceEngineChoices.Strata,
            strataCommandExists: true,
            allowModelDownload: true);

        Assert.True(plan.StopManagedLlama);
        Assert.True(plan.StartStrata);
        Assert.False(plan.StartLlama);
        Assert.Contains("--access", plan.StrataArguments);
        Assert.Contains("local", plan.StrataArguments);
        Assert.Contains("8080", plan.StrataArguments);
        Assert.DoesNotContain("0.0.0.0", plan.StrataArguments);
        Assert.DoesNotContain("lan", plan.StrataArguments);
        Assert.DoesNotContain("--moe-cache-mib", plan.StrataArguments);
        Assert.Equal(["launch", "--stop"], ChatEnginePlan.StopArguments());
    }

    [Fact]
    public void Normalize_Unknown_IsLlamaCpp()
    {
        Assert.Equal(InferenceEngineChoices.LlamaCpp, InferenceEngineChoices.Normalize("cuda"));
        Assert.Equal(InferenceEngineChoices.Strata, InferenceEngineChoices.Normalize(" strata "));
    }
}
