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
    public void Decide_StrataBeforeFiles_KeepsLlamaRunning()
    {
        var plan = ChatEnginePlan.Decide(
            InferenceEngineChoices.Strata,
            strataCommandExists: true,
            allowModelDownload: true,
            dataDirectory: @"H:\Strata-data",
            modelFilesReady: false);

        Assert.True(plan.StartLlama);
        Assert.False(plan.StopManagedLlama);
        Assert.False(plan.StartStrata);
    }

    [Fact]
    public void LaunchArguments_DataDirectory_StaysOneArgument()
    {
        var args = ChatEnginePlan.LaunchArguments(@"H:\Strata-data");

        Assert.Contains(@"H:\Strata-data", args);
        Assert.Contains("--data-dir", args);
        Assert.DoesNotContain("0.0.0.0", args);
        var rejected = ChatEnginePlan.LaunchArguments(@"H:\Strata-data --host 0.0.0.0");
        Assert.DoesNotContain("--data-dir", rejected);
        Assert.DoesNotContain("0.0.0.0", rejected);
        Assert.False(ChatEnginePlan.TryNormalizeDataDirectory(@"H:\Strata-data --host 0.0.0.0", out _));
    }

    [Fact]
    public void CreateLaunchInfo_Cmd_DoesNotUseShellExecute()
    {
        var info = ChatEngineHost.CreateLaunchInfo(
            @"C:\Strata\bin\strata.cmd",
            ChatEnginePlan.LaunchArguments(@"H:\Strata-data"));

        Assert.EndsWith("cmd.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strata.cmd", info.Arguments);
        Assert.Contains("--access", info.Arguments);
        Assert.Contains("local", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.DoesNotContain("0.0.0.0", info.Arguments);
    }

    [Fact]
    public void HasLargeGguf_SmallFile_IsNotReady()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-strata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "tiny.gguf"), "x");
            Assert.False(ChatEnginePlan.HasLargeGguf(dir, minimumBytes: 10));
            File.WriteAllText(Path.Combine(dir, "ready.gguf"), "0123456789");
            Assert.True(ChatEnginePlan.HasLargeGguf(dir, minimumBytes: 10));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Normalize_Unknown_IsLlamaCpp()
    {
        Assert.Equal(InferenceEngineChoices.LlamaCpp, InferenceEngineChoices.Normalize("cuda"));
        Assert.Equal(InferenceEngineChoices.Strata, InferenceEngineChoices.Normalize(" strata "));
    }
}
