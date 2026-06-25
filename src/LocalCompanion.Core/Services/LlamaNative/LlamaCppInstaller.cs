using System.Diagnostics;
using LocalCompanion.Localization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services.LlamaNative;

internal static class LlamaCppInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly string? GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    /// <summary>既知の不良ビルド（固定の「13.3 優先」は使わない）。</summary>
    private static readonly HashSet<string> ExcludedCudaVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "13.2",
    };

    private static readonly Regex LlamaCudaMainRegex = new(
        @"^llama-(?<tag>.+)-bin-win-cuda-(?<ver>\d+\.\d+)-x64\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CudartDllRegex = new(
        @"^cudart-llama-bin-win-cuda-(?<ver>\d+\.\d+)-x64\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> GpuVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "cuda", "hip-radeon", "vulkan", "opencl-adreno",
    };

    static LlamaCppInstaller()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanion/1.0 (+https://github.com/ggml-org/llama.cpp)");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(GitHubToken))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
    }

    internal static string? EnsureInstalled(string root)
    {
        var hw = LlamaHardwareProfile.Current;
        var preferred = LlamaHardwareProfile.ResolvePreferredVariant(hw);
        var found = FindLlamaServerExe(root);

        if (found is not null)
        {
            var installed = ReadInstalledVariant(root);
            if (ShouldUpgradeVariant(installed, preferred))
            {
                NativeLog.WriteKey("Startup.GpuBackendUpgrade", null, LlamaHardwareProfile.DescribeVariant(preferred));
                try
                {
                    Install(root, preferred, hw);
                }
                catch (Exception ex)
                {
                    NativeLog.WriteKey("Startup.GpuBackendUpgradeFailed", null, UserFacingErrorLocalizer.Localize(ex));
                }

                found = FindLlamaServerExe(root);
                installed = ReadInstalledVariant(root);
            }

            if (found is not null)
            {
                EnsureGpuRuntimeIfNeeded(root, found, hw);
                var fallback = TryFallbackFromUnusableGpu(root, found, installed ?? preferred, hw);
                if (fallback is not null)
                {
                    found = fallback;
                    EnsureGpuRuntimeIfNeeded(root, found, hw);
                }
            }
            return found;
        }

        NativeLog.WriteKey("Startup.FirstSetup");
        if (string.Equals(preferred, "hip-radeon", StringComparison.OrdinalIgnoreCase))
            NativeLog.WriteKey("Startup.HipBackendInstall");
        try
        {
            Install(root, preferred, hw);
        }
        catch (Exception ex)
        {
            NativeLog.WriteKey("Startup.LlamaInstallFailed", null, UserFacingErrorLocalizer.Localize(ex));
            return null;
        }

        found = FindLlamaServerExe(root);
        if (found is not null)
            EnsureGpuRuntimeIfNeeded(root, found, hw);
        return found;
    }

    internal static bool HasUsableGpuDevice(string root)
    {
        var exe = FindLlamaServerExe(root);
        if (exe is null)
            return false;
        return GpuDevicesAvailable(Path.GetDirectoryName(exe)!);
    }

    internal static bool HasUsableCudaDevice(string root) => HasUsableGpuDevice(root);

    internal static string? FindLlamaServerExe(string root)
    {
        var env = Environment.GetEnvironmentVariable("LLAMA_SERVER_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);

        var toolsDir = Path.Combine(root, "tools", "llama-cpp");
        if (Directory.Exists(toolsDir))
        {
            foreach (var c in Directory.EnumerateFiles(toolsDir, "llama-server.exe", SearchOption.AllDirectories))
                return c;
        }

        try
        {
            var psi = new ProcessStartInfo("where", "llama-server")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return null;
            var line = proc.StandardOutput.ReadLine();
            proc.WaitForExit(5000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                return line.Trim();
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static void Install(string root, string preferredVariant, LlamaHardwareSnapshot hw)
    {
        var toolsDir = Path.Combine(root, "tools", "llama-cpp");
        Directory.CreateDirectory(toolsDir);

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain(preferredVariant, hw);
        Exception? lastError = null;

        foreach (var variant in chain)
        {
            try
            {
                NativeLog.WriteKey("Startup.EnvCheck", null, LlamaHardwareProfile.DescribeVariant(variant));
                if (TryInstallVariant(root, toolsDir, variant, hw, out var exe))
                {
                    if (!GpuVariants.Contains(variant) || GpuDevicesAvailable(Path.GetDirectoryName(exe)!))
                    {
                        NativeLog.WriteKey("Startup.LlamaServerPath", null, exe);
                        return;
                    }

                    NativeLog.WriteKey("Startup.GpuNotDetectedTryOther", null, LlamaHardwareProfile.DescribeVariant(variant));
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                NativeLog.WriteKey("Startup.VariantSetupFailed", null, LlamaHardwareProfile.DescribeVariant(variant), UserFacingErrorLocalizer.Localize(ex));
            }
        }

        throw new InvalidOperationException(
            LocalizationService.Instance.Get("Error.LlamaSetupFailed"),
            lastError);
    }

    private static bool TryInstallVariant(
        string root,
        string toolsDir,
        string variant,
        LlamaHardwareSnapshot hw,
        out string exePath)
    {
        exePath = string.Empty;
        var release = GetLatestRelease();
        var tag = release.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("release tag missing");

        var assetNames = release.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString() ?? "")
            .ToList();

        string? mainZip;
        string? cudaZip = null;
        string? cudaVersion = null;

        if (string.Equals(variant, "cuda", StringComparison.OrdinalIgnoreCase))
        {
            mainZip = PickLatestLlamaCudaZip(assetNames, tag, majorFilter: null)
                ?? throw new InvalidOperationException($"llama.cpp CUDA ZIP not found (tag={tag})");
            cudaVersion = ExtractCudaVersionFromAsset(mainZip);
            cudaZip = PickCudaDllZip(assetNames, cudaVersion)
                ?? throw new InvalidOperationException($"cudart ZIP not found for cuda-{cudaVersion}");
            NativeLog.WriteKey("Startup.CudaBuildLatest", null, cudaVersion);
        }
        else
        {
            mainZip = PickVariantAsset(assetNames, variant, tag, hw)
                ?? throw new InvalidOperationException($"llama.cpp ZIP not found (variant={variant}, tag={tag})");
        }

        var baseUrl = $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}";
        var tmp = Path.Combine(Path.GetTempPath(), $"llama-cpp-install-{tag}-{variant}");
        if (Directory.Exists(tmp))
            Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);

        try
        {
            var mainZipPath = Path.Combine(tmp, mainZip);
            DownloadZip($"{baseUrl}/{mainZip}", mainZipPath);

            string? cudaZipPath = null;
            if (cudaZip is not null)
            {
                cudaZipPath = Path.Combine(tmp, cudaZip);
                DownloadZip($"{baseUrl}/{cudaZip}", cudaZipPath);
            }

            ClearDirectoryContents(toolsDir);
            ExtractZip(mainZipPath, toolsDir);
            if (cudaZipPath is not null)
                ExtractZip(cudaZipPath, toolsDir);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }

        var exe = FindLlamaServerExe(root)
            ?? throw new InvalidOperationException("llama-server.exe not found after extract");
        exePath = exe;

        var marker = Path.Combine(toolsDir, ".installed.json");
        File.WriteAllText(marker, JsonSerializer.Serialize(new
        {
            tag,
            variant,
            cudaVersion,
            exe,
            arch = LlamaHardwareProfile.GetCpuZipArchSuffix(hw),
            installedAt = DateTimeOffset.Now.ToString("o"),
        }));

        return true;
    }

    private static void EnsureGpuRuntimeIfNeeded(string root, string exe, LlamaHardwareSnapshot hw)
    {
        var exeDir = Path.GetDirectoryName(exe);
        if (string.IsNullOrWhiteSpace(exeDir))
            return;

        var toolsDir = Path.Combine(root, "tools", "llama-cpp");
        if (!Path.GetFullPath(exeDir).StartsWith(Path.GetFullPath(toolsDir), StringComparison.OrdinalIgnoreCase))
            return;

        var variant = ReadInstalledVariant(root) ?? LlamaHardwareProfile.ResolvePreferredVariant(hw);
        if (!GpuVariants.Contains(variant))
        {
            NativeLog.WriteKey("Startup.CpuMode");
            return;
        }

        if (GpuDevicesAvailable(exeDir))
        {
            WriteGpuDetected(variant);
            return;
        }

        NativeLog.WriteKey("Startup.GpuNotDetectedCheckRuntime", null, LlamaHardwareProfile.DescribeVariant(variant));

        if (string.Equals(variant, "cuda", StringComparison.OrdinalIgnoreCase))
            EnsureCudaRuntimeDlls(toolsDir, exeDir, hw);
        else if (string.Equals(variant, "hip-radeon", StringComparison.OrdinalIgnoreCase))
            NativeLog.WriteKey("Startup.HipRuntimeUnavailable");
        else if (string.Equals(variant, "vulkan", StringComparison.OrdinalIgnoreCase))
            NativeLog.WriteKey("Startup.VulkanRuntimeUnavailable");
        else
            NativeLog.WriteKey("Startup.GpuRuntimeUnavailable");
    }

    private static void WriteGpuDetected(string variant)
    {
        var key = variant.ToLowerInvariant() switch
        {
            "cuda" => "Startup.GpuCudaDetected",
            "hip-radeon" => "Startup.GpuHipDetected",
            "vulkan" => "Startup.GpuVulkanDetected",
            "opencl-adreno" => "Startup.GpuOpenClDetected",
            _ => null,
        };

        if (key is not null)
            NativeLog.WriteKey(key);
        else
            NativeLog.WriteKey("Startup.GpuDetected", null, LlamaHardwareProfile.DescribeVariant(variant));
    }

    private static void EnsureCudaRuntimeDlls(string toolsDir, string exeDir, LlamaHardwareSnapshot hw)
    {
        if (HasCudaRuntimeDlls(toolsDir))
        {
            NativeLog.WriteKey("Startup.CudaDllNoGpu");
            return;
        }

        try
        {
            var release = GetLatestRelease();
            var tag = release.GetProperty("tag_name").GetString()
                ?? throw new InvalidOperationException("release tag missing");
            var assetNames = release.GetProperty("assets").EnumerateArray()
                .Select(a => a.GetProperty("name").GetString() ?? "")
                .ToList();

            var mainZip = PickLatestLlamaCudaZip(assetNames, tag, majorFilter: null)
                ?? throw new InvalidOperationException($"llama.cpp CUDA ZIP not found (tag={tag})");
            var cudaVersion = ExtractCudaVersionFromAsset(mainZip);
            var cudaZip = PickCudaDllZip(assetNames, cudaVersion)
                ?? throw new InvalidOperationException($"cudart ZIP not found for cuda-{cudaVersion}");

            NativeLog.WriteKey("Startup.CudaRuntimeRedownload", null, cudaZip);
            var tmp = Path.Combine(Path.GetTempPath(), $"llama-cpp-cuda-runtime-{tag}");
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            try
            {
                DownloadAndExtract(
                    $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}/{cudaZip}",
                    Path.Combine(tmp, cudaZip),
                    toolsDir);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            }

            if (GpuDevicesAvailable(exeDir))
                NativeLog.WriteKey("Startup.GpuCudaDetected");
            else
                NativeLog.WriteKey("Startup.CudaRuntimeStillNoGpu");
        }
        catch (Exception ex)
        {
            NativeLog.WriteKey("Startup.CudaRuntimeCheckFailed", null, UserFacingErrorLocalizer.Localize(ex));
        }
    }

    private static bool ShouldUpgradeVariant(string? installed, string preferred)
    {
        if (string.IsNullOrWhiteSpace(installed))
            return false;
        if (string.Equals(installed, preferred, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.Equals(installed, "cpu", StringComparison.OrdinalIgnoreCase)
            || GpuVariants.Contains(installed)
            || GpuVariants.Contains(preferred);
    }

    private static string? TryFallbackFromUnusableGpu(
        string root,
        string exe,
        string currentVariant,
        LlamaHardwareSnapshot hw)
    {
        if (!GpuVariants.Contains(currentVariant))
            return null;

        var exeDir = Path.GetDirectoryName(exe);
        if (string.IsNullOrWhiteSpace(exeDir) || GpuDevicesAvailable(exeDir))
            return null;

        var chain = LlamaHardwareProfile.BuildInstallFallbackChain(currentVariant, hw);
        var next = chain.FirstOrDefault(v => !string.Equals(v, currentVariant, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(next))
            return null;

        NativeLog.WriteKey("Startup.GpuNotDetectedTryOther", null, LlamaHardwareProfile.DescribeVariant(currentVariant));
        try
        {
            Install(root, next, hw);
            return FindLlamaServerExe(root);
        }
        catch (Exception ex)
        {
            NativeLog.WriteKey("Startup.GpuBackendUpgradeFailed", null, UserFacingErrorLocalizer.Localize(ex));
            return null;
        }
    }

    private static string? ReadInstalledVariant(string root)
    {
        var marker = Path.Combine(root, "tools", "llama-cpp", ".installed.json");
        if (!File.Exists(marker))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            return doc.RootElement.TryGetProperty("variant", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasCudaRuntimeDlls(string toolsDir) =>
        Directory.Exists(toolsDir)
        && Directory.EnumerateFiles(toolsDir, "cudart64_*.dll", SearchOption.TopDirectoryOnly).Any();

    private static void DownloadAndExtract(string url, string zipPath, string destDir)
    {
        DownloadZip(url, zipPath);
        ExtractZip(zipPath, destDir);
    }

    private static void DownloadZip(string url, string zipPath)
    {
        var fileName = Path.GetFileName(zipPath);
        NativeLog.WriteKey("Startup.Download.Fetch", null, fileName);

        using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
        using (var fs = File.Create(zipPath))
            DownloadProgress.CopyStream(stream, fs, "Startup.Download.Label.LlamaCpp", total, fileName);
    }

    private static void ExtractZip(string zipPath, string destDir)
    {
        StartupProgress.ReportKey("Startup.Extracting");
        ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
    }

    private static void ClearDirectoryContents(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(dir))
            File.Delete(file);
        foreach (var subDir in Directory.EnumerateDirectories(dir))
            Directory.Delete(subDir, recursive: true);
    }

    private static JsonElement GetLatestRelease()
    {
        Exception? last = null;
        var url = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return Http.GetFromJsonAsync<JsonElement>(url).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                last = ex;
                if (attempt < 3)
                    Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < 3)
                    Thread.Sleep(TimeSpan.FromSeconds(attempt));
            }
        }

        throw new InvalidOperationException(
            "llama.cpp の最新情報を取得できませんでした。ネットワーク制限またはダウンロード元の一時的な制限の可能性があります。しばらく待ってから再実行してください。",
            last);
    }

    private static bool GpuDevicesAvailable(string workDir)
    {
        var cli = Path.Combine(workDir, "llama-cli.exe");
        if (!File.Exists(cli))
            return false;

        try
        {
            var psi = new ProcessStartInfo(cli, "--list-devices")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;

            var output = proc.StandardOutput.ReadToEnd();
            output += proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(10000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Equals("Available devices:", StringComparison.OrdinalIgnoreCase));
            return lines.Any();
        }
        catch
        {
            return false;
        }
    }

    private static string? PickVariantAsset(List<string> names, string variant, string tag, LlamaHardwareSnapshot hw)
    {
        if (names.Count == 0)
        {
            NativeLog.WriteKey("Startup.Log.NoReleaseAssets");
            return null;
        }

        var arch = LlamaHardwareProfile.GetCpuZipArchSuffix(hw);
        var pattern = variant switch
        {
            "hip-radeon" => $"llama-{tag}-bin-win-hip-radeon-x64.zip",
            "vulkan" => $"llama-{tag}-bin-win-vulkan-x64.zip",
            "opencl-adreno" => $"llama-{tag}-bin-win-opencl-adreno-arm64.zip",
            _ => $"llama-{tag}-bin-win-cpu-{arch}.zip",
        };

        var hit = names.FirstOrDefault(n => string.Equals(n, pattern, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            NativeLog.WriteKey("Startup.Log.ZipNotFound", null, variant, tag, names.Count);
        return hit;
    }

    private static string? PickLatestLlamaCudaZip(List<string> names, string tag, int? majorFilter)
    {
        var best = PickLatestCudaCandidate(names, tag, LlamaCudaMainRegex, majorFilter);
        if (best is null && names.Count > 0)
            NativeLog.WriteKey("Startup.Log.CudaZipNotFound", null, tag, names.Count);
        return best;
    }

    private static string? PickCudaDllZip(List<string> names, string cudaVersion)
    {
        var exact = $"cudart-llama-bin-win-cuda-{cudaVersion}-x64.zip";
        var hit = names.FirstOrDefault(n => string.Equals(n, exact, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
            return hit;

        return names.FirstOrDefault(n =>
        {
            var m = CudartDllRegex.Match(n);
            return m.Success
                && string.Equals(m.Groups["ver"].Value, cudaVersion, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string? PickLatestCudaCandidate(
        List<string> names,
        string tag,
        Regex assetRegex,
        int? majorFilter)
    {
        string? bestName = null;
        Version? bestVer = null;

        foreach (var name in names)
        {
            var m = assetRegex.Match(name);
            if (!m.Success || !string.Equals(m.Groups["tag"].Value, tag, StringComparison.OrdinalIgnoreCase))
                continue;

            var verText = m.Groups["ver"].Value;
            if (ExcludedCudaVersions.Contains(verText))
                continue;

            if (!Version.TryParse(verText, out var ver))
                continue;

            if (majorFilter is int major && ver.Major != major)
                continue;

            if (bestVer is null || ver > bestVer)
            {
                bestVer = ver;
                bestName = name;
            }
        }

        return bestName;
    }

    private static string ExtractCudaVersionFromAsset(string assetName)
    {
        var m = LlamaCudaMainRegex.Match(assetName);
        if (!m.Success)
            throw new InvalidOperationException($"CUDA version not found in asset: {assetName}");
        return m.Groups["ver"].Value;
    }
}
