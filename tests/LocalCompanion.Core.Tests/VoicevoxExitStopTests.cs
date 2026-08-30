using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxExitStopTests
{
    [Fact]
    public void ShouldStopProcessOnAppExit_RunExe_OnlyWhenManagedSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VOICEVOX-" + Guid.NewGuid().ToString("N"));
        var run = Path.Combine(dir, "run.exe");
        var engine = Path.Combine(dir, "ENGINE.exe");
        Assert.False(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(run, null));
        Assert.True(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(run, engine));
    }

    [Fact]
    public void ShouldStopProcessOnAppExit_EngineExe_OnlyWhenManagedPathMatches()
    {
        var engine = Path.Combine(Path.GetTempPath(), "VOICEVOX ENGINE", "VOICEVOX ENGINE.exe");
        Assert.False(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(engine, null));
        Assert.True(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(engine, engine));
    }

    [Fact]
    public void ShouldStopProcessOnAppExit_OtherInstallRunExe_NotStopped()
    {
        var otherRun = Path.Combine(Path.GetTempPath(), "OTHER-VV-" + Guid.NewGuid().ToString("N"), "run.exe");
        var managed = Path.Combine(Path.GetTempPath(), "MANAGED-VV-" + Guid.NewGuid().ToString("N"), "ENGINE.exe");
        Assert.False(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(otherRun, managed));
    }

    [Fact]
    public void ShouldStopProcessOnAppExit_EditorExe_NotUnlessManaged()
    {
        var editor = Path.Combine(Path.GetTempPath(), "VOICEVOX", "VOICEVOX.exe");
        Assert.False(VoicevoxLifecycleService.ShouldStopProcessOnAppExit(editor, null));
    }
}

public sealed class LlamaExitStopTests
{
    [Fact]
    public void StopLlamaProcesses_PidOnlyWithoutMarker_DoesNotSkipPid()
    {
        var dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var selfPid = Environment.ProcessId;
            ManagedLlamaProcess.WritePid(dir, selfPid);

            LlamaServerNativeHost.StopLlamaProcesses(dir, waitAfterKill: false, requireMarker: false);

            Assert.Equal(selfPid, ManagedLlamaProcess.TryReadPid(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
