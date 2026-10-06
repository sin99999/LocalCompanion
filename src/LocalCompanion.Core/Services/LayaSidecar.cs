using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using LocalCompanion.Models;
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
    private static string _signature = "";

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
        StartIfEnabled(paths);
    }

    public static void Apply(AppPaths paths, bool enabled)
    {
        MigrateLegacyHome(paths.Root);
        if (!enabled)
        {
            _signature = "off";
            Interlocked.Exchange(ref _kicked, 0);
            Interlocked.Increment(ref _runId);
            Stop();
            return;
        }

        StartIfEnabled(paths);
    }

    private static void StartIfEnabled(AppPaths paths)
    {
        var settings = LoadSettings(paths.Root);
        if (!settings.JuliaDecisionEnabled)
            return;

        var signature = JuliaDevicePreferences.Normalize(settings.JuliaDevice)
            + "|" + (settings.JuliaAutoUpdate ? "1" : "0");
        if (signature == _signature && Volatile.Read(ref _kicked) == 1)
            return;

        _signature = signature;
        Interlocked.Exchange(ref _kicked, 1);
        Interlocked.Increment(ref _runId);
        _stop = false;
        _ready = false;
        _root = paths.Root;
        KillTracked(Home(paths.Root));
        var runId = Volatile.Read(ref _runId);
        _ = Task.Run(() => Run(paths.Root, runId));
    }

    public static void Stop()
    {
        _stop = true;
        _ready = false;
        KillTracked(RootOrNull());
    }

    private static AppSettingsDto LoadSettings(string root)
    {
        try
        {
            var configured = LlamaInstallConfig.Load(root).DataDirectory;
            var dir = AppPaths.ResolveUserDataDirectory(configured);
            return AppSettingsStore.LoadOrDefault(dir);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Julia settings unreadable; using defaults");
            return new AppSettingsDto();
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

    public readonly record struct JuliaRouteAnswer(string? Label, double Confidence);

    public static async Task<JuliaRouteAnswer> TryRouteAsync(string message, CancellationToken ct)
    {
        var json = await PostAsync(LayaDecision.BuildRouteChoice(message), ct);
        if (json is null)
            return new(null, 0);
        var choice = LayaDecision.ReadChoice(json, "route");
        StartupLog.Write(LayaDecision.FormatRoute(choice));
        if (choice is not { } picked)
            return new(null, 0);
        return new(picked.Label, picked.Confidence);
    }

    public static async Task<RagConversationMode> TryUpgradeProbeAsync(string message, CancellationToken ct)
    {
        var answer = await TryRouteAsync(message, ct);
        var choice = JuliaRoutePolicy.ChoiceFromLabel(answer.Label, answer.Confidence);
        if (choice == JuliaRouteChoice.Notes)
            return RagConversationMode.SoftTopic;
        return RagConversationMode.Probe;
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

            var settings = LoadSettings(root);
            var model = EnsureModel(home, settings.JuliaAutoUpdate, out var replaced);
            if (model is null || Stale(runId))
                return;

            var preference = JuliaDevicePreferences.Normalize(settings.JuliaDevice);
            string? gpuExe = null;
            var gpuReady = false;
            if (preference != JuliaDevicePreferences.Cpu)
            {
                gpuExe = FindGpuServer(root);
                gpuReady = gpuExe is not null;
            }

            long? freeVram = preference == JuliaDevicePreferences.Auto
                ? LlamaHardwareProfile.TryReadNvidiaFreeVramBytes()
                : null;
            var decision = JuliaPlacement.Decide(
                preference,
                new FileInfo(model).Length,
                freeVram,
                gpuReady);
            StartupLog.Write("Julia placement " + (decision.UseGpu ? "gpu" : "cpu") + " " + decision.Reason);

            var exe = decision.UseGpu ? gpuExe : EnsureCpuServer(home);
            var place = decision.UseGpu ? "gpu" : "cpu";
            if (exe is null || Stale(runId))
                return;

            if (!replaced
                && HealthOk()
                && TrackedAlive(home)
                && ModelMarkerMatches(home)
                && PlaceMatches(home, place))
            {
                _ready = true;
                StartupLog.Write("decision server already running.");
                return;
            }

            var layers = decision.UseGpu ? JuliaPlacement.GpuLayers : 0;
            if (!StartServer(home, exe, model, layers, place))
                return;
            if (WaitReady(home, runId))
                return;

            if (!decision.UseGpu || Stale(runId))
                return;

            StartupLog.Write("Julia GPU did not become ready; starting on CPU.");
            var cpuExe = EnsureCpuServer(home);
            if (cpuExe is null || Stale(runId))
                return;
            if (!StartServer(home, cpuExe, model, 0, "cpu"))
                return;
            WaitReady(home, runId);
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

    private static string? EnsureModel(string home, bool autoUpdate, out bool replaced)
    {
        replaced = false;
        var dest = Path.Combine(home, ModelFileName);
        var ready = File.Exists(dest) && new FileInfo(dest).Length >= MinModelBytes;
        var tokenPath = Path.Combine(home, ".julia-source");
        var saved = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : null;
        var remote = ready && !autoUpdate ? null : TryReadRemoteToken(ModelUrl);
        var action = JuliaPlacement.DecideUpdate(autoUpdate, ready, saved, remote);
        if (action == JuliaModelAction.Remember && remote is not null)
        {
            File.WriteAllText(tokenPath, remote);
            return dest;
        }

        if (action == JuliaModelAction.Keep && ready)
            return dest;

        StartupLog.Write("decision downloading " + ModelFileName);
        if (!Download(ModelUrl, dest, MinModelBytes))
            return ready ? dest : null;

        replaced = true;
        remote ??= TryReadRemoteToken(ModelUrl);
        if (remote is not null)
            File.WriteAllText(tokenPath, remote);
        return dest;
    }

    private static string? TryReadRemoteToken(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = DownloadHttp.Send(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            var etag = response.Headers.ETag?.Tag;
            if (string.IsNullOrWhiteSpace(etag)
                && response.Headers.TryGetValues("X-Linked-Etag", out var linked))
            {
                etag = linked.FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(etag))
                return etag.Trim().Trim('"');

            var length = response.Content.Headers.ContentLength;
            return length is > 0 ? "len:" + length.Value : null;
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Julia update check failed");
            return null;
        }
    }

    private static string? FindGpuServer(string root)
    {
        var exe = LlamaCppInstaller.FindLlamaServerExe(root);
        if (exe is null || IsJuliaCpuCopy(root, exe))
            return null;

        var marker = Path.Combine(root, "tools", "llama-cpp", ".installed.json");
        if (!File.Exists(marker))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(marker));
            var variant = doc.RootElement.TryGetProperty("variant", out var value)
                ? value.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(variant)
                || variant.Equals("cpu", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        return LlamaCppInstaller.HasUsableGpuDevice(root) ? exe : null;
    }

    private static bool IsJuliaCpuCopy(string root, string exe)
    {
        var juliaBin = Path.GetFullPath(Path.Combine(root, "tools", "Julia"));
        var full = Path.GetFullPath(exe);
        return full.StartsWith(juliaBin + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(juliaBin + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

    private static bool WaitReady(string home, int runId)
    {
        for (var i = 0; i < 40 && !Stale(runId); i++)
        {
            if (HealthOk())
            {
                _ready = true;
                StartupLog.Write("decision ready " + ModelId + " on port " + Port);
                return true;
            }

            Thread.Sleep(500);
            if (!TrackedAlive(home))
            {
                StartupLog.Write("Laya server exited before ready.");
                return false;
            }
        }

        return false;
    }

    private static bool StartServer(string home, string exe, string model, int gpuLayers, string place)
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
        psi.ArgumentList.Add(gpuLayers.ToString());
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(threads.ToString());

        var proc = Process.Start(psi);
        if (proc is null)
            return false;
        ChildProcessJob.Assign(proc);
        File.WriteAllText(PidPath(home), proc.Id.ToString());
        File.WriteAllText(ModelMarkerPath(home), ModelId);
        File.WriteAllText(PlacePath(home), place);
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

    private static string PlacePath(string home) => Path.Combine(home, ".julia-place");

    private static bool PlaceMatches(string home, string place)
    {
        var path = PlacePath(home);
        return File.Exists(path)
            && File.ReadAllText(path).Trim().Equals(place, StringComparison.Ordinal);
    }

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
