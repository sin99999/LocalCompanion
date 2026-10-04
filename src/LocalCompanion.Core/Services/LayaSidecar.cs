using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Services;

/// <summary>
/// 会話役とは別の、CPU の llama-server で判断だけを置く。
/// 重みは Julia-1。Laya は英語だけで、日本語の会話が半々だった。
/// 会話役の GGUF フォルダには置かない。準備が終わるまでは、今までの動きのまま。
/// </summary>
internal static class LayaSidecar
{
    /// <summary>2026-10-03 時点で /v1/systemone がある llama.cpp。会話役のバイナリは取り替えない。</summary>
    internal const string LlamaTag = "b11366";

    internal const int Port = 8091;
    private const string ModelFileName = "Julia-1-Q8_0.gguf";
    private const string ModelId = "Julia-1-Q8_0";
    private const long MinModelBytes = 150L * 1024 * 1024;
    private const long MinFreeBytes = 400L * 1024 * 1024;
    private const string ModelUrl =
        "https://huggingface.co/ggml-org/Julia-1-GGUF/resolve/main/Julia-1-Q8_0.gguf";

    private static readonly HttpClient DownloadHttp = CreateDownloadHttp();
    private static readonly HttpClient DecisionHttp = new() { Timeout = TimeSpan.FromSeconds(2) };

    private static readonly object Gate = new();
    private static int _kicked;
    private static int _runId;
    private static volatile bool _stop;
    private static volatile bool _ready;
    private static string? _root;

    public static bool IsReady => _ready && !_stop;

    private static HttpClient CreateDownloadHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanion/1.3");
        return http;
    }

    public static void Kick(AppPaths paths)
    {
        MigrateLegacyHome(paths.Root);
        if (!IsDecisionEnabled(paths))
            return;
        if (Interlocked.Exchange(ref _kicked, 1) == 1)
            return;
        _stop = false;
        _root = paths.Root;
        var runId = Interlocked.Increment(ref _runId);
        _ = Task.Run(() => Run(paths.Root, runId));
    }

    public static void Apply(AppPaths paths, bool enabled)
    {
        MigrateLegacyHome(paths.Root);
        if (!enabled)
        {
            Interlocked.Increment(ref _runId);
            Stop();
            Interlocked.Exchange(ref _kicked, 0);
            return;
        }

        Kick(paths);
    }

    public static void Stop()
    {
        _stop = true;
        _ready = false;
        KillTracked(RootOrNull());
    }

    private static bool IsDecisionEnabled(AppPaths paths)
    {
        try
        {
            var configured = LlamaInstallConfig.Load(paths.Root).DataDirectory;
            var dir = AppPaths.ResolveUserDataDirectory(configured);
            return AppSettingsStore.LoadOrDefault(dir).JuliaDecisionEnabled;
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Julia decision setting unreadable; leaving it on");
            return true;
        }
    }

    public static async Task<bool> ShouldSkipPassageAsync(string passage, CancellationToken ct)
    {
        if (!LayaDecision.UseYesNoSkip)
            return false;
        var yes = await TryNoulAsync(
            passage,
            "Does this passage contain a fact a person would later search for? No for titles, menus, and boilerplate.",
            ct);
        return LayaDecision.ShouldSkip(yes);
    }

    public static async Task<bool> QuestionMissesPassageAsync(string passage, string question, CancellationToken ct)
    {
        if (!LayaDecision.UseYesNoSkip)
            return false;
        var yes = await TryNoulAsync(
            passage + "\n\nQuestion: " + question,
            "Is this question answered by the passage above?",
            ct);
        return LayaDecision.ShouldSkip(yes);
    }

    public static async Task<bool> ShouldSkipMemoryAsync(string transcript, CancellationToken ct)
    {
        if (!LayaDecision.UseYesNoSkip)
            return false;
        var yes = await TryNoulAsync(
            transcript,
            "Does this conversation contain a personal fact about the user worth remembering later? No for small talk.",
            ct);
        return LayaDecision.ShouldSkip(yes);
    }

    public static async Task<RagConversationMode> TryUpgradeProbeAsync(string message, CancellationToken ct)
    {
        var json = await PostAsync(LayaDecision.BuildSearchChoice(message), ct);
        if (json is null)
            return RagConversationMode.Probe;
        var choice = LayaDecision.ReadChoice(json, "route");
        StartupLog.Write(LayaDecision.FormatRoute(choice));
        if (!LayaDecision.ShouldSearchNotes(choice))
            return RagConversationMode.Probe;
        return RagConversationMode.SoftTopic;
    }

    private static async Task<double?> TryNoulAsync(string state, string instructions, CancellationToken ct)
    {
        var json = await PostAsync(LayaDecision.BuildNoul("q", state, instructions), ct);
        return json is null ? null : LayaDecision.ReadNoul(json, "q");
    }

    private static async Task<string?> PostAsync(string body, CancellationToken ct)
    {
        if (!IsReady)
            return null;
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await DecisionHttp.PostAsync(
                $"http://127.0.0.1:{Port}/v1/systemone", content, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Laya decision failed");
            return null;
        }
    }

    private static void Run(string root, int runId)
    {
        try
        {
            if (Stale(runId))
                return;
            var home = Home(root);
            Directory.CreateDirectory(home);
            if (!HasFreeSpace(home))
            {
                StartupLog.Write("Laya skipped: not enough free disk.");
                return;
            }

            var exe = EnsureCpuServer(home);
            if (exe is null || Stale(runId))
                return;
            var model = EnsureModel(home);
            if (model is null || Stale(runId))
                return;

            if (HealthOk() && TrackedAlive(home) && ModelMarkerMatches(home))
            {
                _ready = true;
                StartupLog.Write("decision server already running.");
                return;
            }

            if (!StartServer(home, exe, model))
                return;

            for (var i = 0; i < 40 && !Stale(runId); i++)
            {
                if (HealthOk())
                {
                    _ready = true;
                    StartupLog.Write("decision ready " + ModelId + " on port " + Port);
                    return;
                }

                Thread.Sleep(500);
                if (!TrackedAlive(home))
                {
                    StartupLog.Write("Laya server exited before ready.");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Laya sidecar failed");
        }
    }

    private static bool Stale(int runId) => _stop || runId != Volatile.Read(ref _runId);

    private static string? EnsureCpuServer(string home)
    {
        var bin = Path.Combine(home, "bin");
        var exe = FindExe(bin);
        var marker = Path.Combine(home, ".llama-tag");
        if (exe is not null && File.Exists(marker) && File.ReadAllText(marker).Trim() == LlamaTag)
            return exe;

        var zipName = $"llama-{LlamaTag}-bin-win-cpu-x64.zip";
        var url = $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaTag}/{zipName}";
        var zipPath = Path.Combine(home, zipName);
        StartupLog.Write("Laya downloading decision server " + LlamaTag);
        if (!Download(url, zipPath, minBytes: 1_000_000))
            return null;
        if (Directory.Exists(bin))
            Directory.Delete(bin, true);
        Directory.CreateDirectory(bin);
        ZipFile.ExtractToDirectory(zipPath, bin, overwriteFiles: true);
        try { File.Delete(zipPath); } catch { /* ignore */ }
        exe = FindExe(bin);
        if (exe is null)
            return null;
        File.WriteAllText(marker, LlamaTag);
        return exe;
    }

    private static string? EnsureModel(string home)
    {
        var dest = Path.Combine(home, ModelFileName);
        if (File.Exists(dest) && new FileInfo(dest).Length >= MinModelBytes)
            return dest;

        StartupLog.Write("decision downloading " + ModelFileName);
        if (!Download(ModelUrl, dest, MinModelBytes))
            return null;
        return dest;
    }

    private static bool Download(string url, string dest, long minBytes)
    {
        var partial = dest + ".partial";
        try
        {
            if (File.Exists(partial))
                File.Delete(partial);
            using var response = DownloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using (var stream = response.Content.ReadAsStream())
            using (var file = File.Create(partial))
            {
                stream.CopyTo(file);
            }

            if (!File.Exists(partial) || new FileInfo(partial).Length < minBytes)
            {
                TryDelete(partial);
                StartupLog.Write("Laya download too small: " + url);
                return false;
            }

            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(partial, dest);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            StartupLog.Write(ex, "Laya download failed");
            return false;
        }
    }

    private static bool StartServer(string home, string exe, string model)
    {
        KillTracked(home);
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? home,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(model);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(Port.ToString());
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("512");
        psi.ArgumentList.Add("-ngl");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(threads.ToString());

        var proc = Process.Start(psi);
        if (proc is null)
            return false;
        ChildProcessJob.Assign(proc);
        File.WriteAllText(PidPath(home), proc.Id.ToString());
        File.WriteAllText(ModelMarkerPath(home), ModelId);
        TryDelete(Path.Combine(home, "Laya-Q8_0.gguf"));
        return true;
    }

    private static bool HealthOk()
    {
        try
        {
            using var response = DecisionHttp.GetAsync($"http://127.0.0.1:{Port}/health")
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void KillTracked(string? home)
    {
        if (home is null)
            return;
        var path = PidPath(home);
        if (!File.Exists(path) || !int.TryParse(File.ReadAllText(path).Trim(), out var pid))
            return;
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.HasExited)
                return;
            if (!proc.ProcessName.Equals("llama-server", StringComparison.OrdinalIgnoreCase))
                return;
            proc.Kill(entireProcessTree: true);
        }
        catch
        {
            /* already gone */
        }
    }

    private static bool TrackedAlive(string home)
    {
        var path = PidPath(home);
        if (!File.Exists(path) || !int.TryParse(File.ReadAllText(path).Trim(), out var pid))
            return false;
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited
                   && proc.ProcessName.Equals("llama-server", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFreeSpace(string home)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(home));
        if (string.IsNullOrEmpty(root))
            return true;
        return new DriveInfo(root).AvailableFreeSpace >= MinFreeBytes;
    }

    private static string? FindExe(string dir)
    {
        if (!Directory.Exists(dir))
            return null;
        var direct = Path.Combine(dir, "llama-server.exe");
        if (File.Exists(direct))
            return direct;
        return Directory.EnumerateFiles(dir, "llama-server.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string Home(string root)
    {
        MigrateLegacyHome(root);
        return Path.Combine(root, "tools", "Julia");
    }

    internal static void MigrateLegacyHome(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        var legacy = Path.Combine(root, "tools", "laya");
        var julia = Path.Combine(root, "tools", "Julia");
        if (!Directory.Exists(legacy))
            return;
        if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(julia), StringComparison.OrdinalIgnoreCase))
            return;

        KillTracked(legacy);
        TryDelete(PidPath(legacy));
        try
        {
            if (!Directory.Exists(julia))
            {
                Directory.CreateDirectory(Path.Combine(root, "tools"));
                Directory.Move(legacy, julia);
                return;
            }

            foreach (var file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(legacy, file);
                var dest = Path.Combine(julia, relative);
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                if (!File.Exists(dest))
                    File.Move(file, dest);
            }

            Directory.Delete(legacy, recursive: true);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Julia folder rename failed");
        }
    }

    private static string PidPath(string home) => Path.Combine(home, "server.pid");

    private static string ModelMarkerPath(string home) => Path.Combine(home, ".decision-model");

    private static bool ModelMarkerMatches(string home)
    {
        var path = ModelMarkerPath(home);
        return File.Exists(path)
            && File.ReadAllText(path).Trim().Equals(ModelId, StringComparison.Ordinal);
    }

    private static string? RootOrNull()
    {
        lock (Gate)
            return _root;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
