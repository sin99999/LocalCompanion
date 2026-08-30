using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ManagedLlamaProcessTests
{
    [Fact]
    public void WritePid_AndTryReadPid_RoundTrip()
    {
        var dir = CreateTempDir();
        try
        {
            ManagedLlamaProcess.WritePid(dir, 4242);

            Assert.Equal(4242, ManagedLlamaProcess.TryReadPid(dir));
            Assert.True(File.Exists(ManagedLlamaProcess.ResolvePidPath(dir)));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryReadPid_ReturnsNullForMissingOrInvalid()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.Null(ManagedLlamaProcess.TryReadPid(dir));

            File.WriteAllText(ManagedLlamaProcess.ResolvePidPath(dir), "not-a-pid");
            Assert.Null(ManagedLlamaProcess.TryReadPid(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void ClearTracking_RemovesPidFile()
    {
        var dir = CreateTempDir();
        try
        {
            ManagedLlamaProcess.WritePid(dir, 1);
            ManagedLlamaProcess.ClearTracking(dir);

            Assert.Null(ManagedLlamaProcess.TryReadPid(dir));
            Assert.False(File.Exists(ManagedLlamaProcess.ResolvePidPath(dir)));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void StopManaged_PidOnlyWithoutMarker_StillChecksPid()
    {
        // AppBootstrap 終了がマーカー無し＋PID を落とすための退行防止（名前が llama-server でない PID は殺さない）。
        var dir = CreateTempDir();
        try
        {
            var selfPid = Environment.ProcessId;
            ManagedLlamaProcess.WritePid(dir, selfPid);

            Assert.False(ManagedLlamaProcess.StopManaged(dir, waitAfterKill: false, requireMarker: false));
            Assert.Equal(selfPid, ManagedLlamaProcess.TryReadPid(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void StopManaged_RequireMarkerTrue_PidOnlyWithoutMarker_StillChecksPid()
    {
        var dir = CreateTempDir();
        try
        {
            var selfPid = Environment.ProcessId;
            ManagedLlamaProcess.WritePid(dir, selfPid);

            Assert.False(ManagedLlamaProcess.StopManaged(dir, waitAfterKill: false, requireMarker: true));
            Assert.Equal(selfPid, ManagedLlamaProcess.TryReadPid(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void StopManaged_WithoutMarkerOrPid_DoesNotKill()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.False(ManagedLlamaProcess.StopManaged(dir, waitAfterKill: false));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void StopManaged_WhenPidAliveButNotLlama_KeepsTracking()
    {
        var dir = CreateTempDir();
        try
        {
            var selfPid = Environment.ProcessId;
            ManagedLlamaProcess.WritePid(dir, selfPid);
            File.WriteAllText(LlamaManagedMarker.ResolvePath(dir), "1");

            Assert.False(ManagedLlamaProcess.StopManaged(dir, waitAfterKill: false));
            Assert.Equal(selfPid, ManagedLlamaProcess.TryReadPid(dir));
            Assert.True(File.Exists(LlamaManagedMarker.ResolvePath(dir)));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void IsTrackedLlamaAlive_FalseWhenPidMissing()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.False(ManagedLlamaProcess.IsTrackedLlamaAlive(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}
