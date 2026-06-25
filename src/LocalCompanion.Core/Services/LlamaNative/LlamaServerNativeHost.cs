using System.Diagnostics;
using LocalCompanion.Localization;
using LocalCompanion.Services;

namespace LocalCompanion.Services.LlamaNative;

/// <summary>PowerShell を使わず llama-server を起動する。</summary>
public static class LlamaServerNativeHost
{
    public static int EnsureAndStart(AppPaths paths)
    {
        var root = paths.Root;
        var settings = LlamaInstallConfig.Load(root);
        var modelsDir = paths.ModelsDirectory;

        var exe = LlamaCppInstaller.EnsureInstalled(root);
        if (exe is null)
            return 1;

        DefaultModelDownloader.TryBootstrap(root);

        var dataDir = AppPaths.ResolveUserDataDirectory(settings.DataDirectory);
        var extraFolders = ModelLibrarySettings.EnumerateModelFolders(modelsDir, dataDir)
            .Where(d => !string.Equals(d, modelsDir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var resolved = MmprojSupport.ResolveModelPaths(
            root, modelsDir, null, null, settings.ModelGgufPath, settings.MmprojGgufPath, extraFolders);

        if (resolved.ModelPath is null)
        {
            NativeLog.WriteKey("Startup.NoChatGgufSkip");
            return 0;
        }

        var modelPath = resolved.ModelPath;
        var modelFileName = Path.GetFileName(modelPath);
        MmprojSupport.TryEnsureKnownMmproj(root, modelFileName, modelPath);
        resolved = MmprojSupport.ResolveModelPaths(
            root, modelsDir, modelPath, resolved.MmprojPath, null, null, extraFolders);

        if (resolved.ModelPath is null)
        {
            NativeLog.WriteKey("Startup.NoChatGgufSkip");
            return 0;
        }

        modelPath = resolved.ModelPath;
        var mmprojPath = resolved.MmprojPath;

        var modelSizeGb = new FileInfo(modelPath).Length / (1024.0 * 1024 * 1024);
        var hasMmproj = !string.IsNullOrEmpty(mmprojPath) && File.Exists(mmprojPath);

        // 最終的な -c は起動済み判定（.last-ctx 比較）より前に確定させる
        var requestedContext = settings.ContextLength;
        var context = LlamaContextPolicy.CapForModel(requestedContext, modelSizeGb, hasMmproj);
        if (context != requestedContext)
            NativeLog.WriteKeyLogOnly("Startup.ContextCapped", context, requestedContext);

        var hasUsableGpu = LlamaCppInstaller.HasUsableGpuDevice(root);
        SystemMemoryGuard.EnsureCanRunModelOrThrow(modelPath, mmprojPath, context, hasUsableGpu);

        if (settings.GpuLayers > 0 && !hasUsableGpu)
            NativeLog.WriteKey("Startup.GpuLayersNoDevice");

        var port = settings.Port;
        var toolsDir = paths.ToolsDirectory;
        Directory.CreateDirectory(toolsDir);

        var ctxMarker = Path.Combine(toolsDir, ".last-ctx");
        var modelMarker = Path.Combine(toolsDir, ".last-model");
        var managedMarker = LlamaManagedMarker.ResolvePath(toolsDir);
        var configuredGpuLayers = hasUsableGpu ? Math.Max(0, settings.GpuLayers) : 0;
        var modelKey = modelPath.ToLowerInvariant()
            + "|" + (mmprojPath ?? "").ToLowerInvariant()
            + "|ngl=" + configuredGpuLayers;

        if (LlamaServerHealth.IsModelReady(port))
        {
            var lastCtx = ReadIntMarker(ctxMarker);
            var lastModel = ReadStringMarker(modelMarker);
            if (lastCtx == context && lastModel == modelKey)
            {
                SetManagedFlag(managedMarker);
                NativeLog.WriteKey("Startup.LlamaAlreadyRunning", null, context);
                return 0;
            }
            NativeLog.WriteKey("Startup.LlamaRestart");
            StopLlamaProcesses();
        }

        var extraArgs = new List<string>();
        if (modelSizeGb >= LlamaContextPolicy.LargeModelGbThreshold && hasMmproj)
        {
            extraArgs.AddRange(["-np", "1", "--fit", "on", "--fit-target", "2048"]);
        }
        else if (modelSizeGb >= LlamaContextPolicy.LargeModelGbThreshold)
        {
            extraArgs.AddRange(["-np", "1"]);
        }

        var modelBase = Path.GetFileName(modelPath);
        var args = new List<string>
        {
            "-m", modelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(),
            "-c", context.ToString(),
            "-ngl", settings.GpuLayers.ToString(),
            "--jinja",
            "--embeddings",
            "--pooling", "last",
            "--reasoning", "auto",
            "--reasoning-format", "deepseek",
        };
        if (modelBase.Contains("E2B", StringComparison.OrdinalIgnoreCase)
            || modelBase.Contains("26B", StringComparison.OrdinalIgnoreCase)
            || modelBase.Contains("gemma-4", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["--reasoning-budget", "4096"]);
        }
        args.AddRange(extraArgs);
        if (hasMmproj && mmprojPath is not null)
            args.AddRange(["--mmproj", mmprojPath]);

        var logFile = Path.Combine(toolsDir, "llama-server.log");
        var waitSec = Math.Clamp(context / 64, 180, 600);
        var gpuLayerAttempts = LlamaGpuLayerPolicy.BuildLayerAttempts(configuredGpuLayers, hasUsableGpu);
        for (var attemptIndex = 0; attemptIndex < gpuLayerAttempts.Count; attemptIndex++)
        {
            var gpuLayers = gpuLayerAttempts[attemptIndex];
            if (gpuLayers != configuredGpuLayers)
                NativeLog.WriteKey("Startup.GpuLayersRetry", null, gpuLayers);

            SetGpuLayers(args, gpuLayers);
            StartLlamaProcess(exe, args, Path.GetDirectoryName(exe)!, logFile);
            // 起動待ち中にアプリが終了しても停止対象になるよう、先に管理マーカーを立てる
            SetManagedFlag(managedMarker);

            var deadline = DateTime.UtcNow.AddSeconds(waitSec);
            var started = DateTime.UtcNow;
            NativeLog.WriteKey("Startup.LlamaWaiting", null, waitSec);
            NativeLog.WriteKey("Startup.LlamaLoadingModel", 5, 5);
            while (DateTime.UtcNow < deadline)
            {
                if (LlamaServerHealth.IsModelReady(port))
                {
                    WriteMarker(ctxMarker, context.ToString());
                    WriteMarker(modelMarker, modelKey);
                    SetManagedFlag(managedMarker);
                    NativeLog.WriteKey("Startup.LlamaReady", 100);
                    return 0;
                }

                if (Process.GetProcessesByName("llama-server").Length == 0)
                {
                    NativeLog.WriteKey("Startup.LlamaProcessExited");
                    if (attemptIndex + 1 < gpuLayerAttempts.Count)
                        break;

                    TailLog(logFile);
                    return 1;
                }

                var elapsed = (DateTime.UtcNow - started).TotalSeconds;
                var fraction = Math.Min(1.0, elapsed / waitSec);
                var pct = 5 + fraction * 90;
                NativeLog.WriteKey("Startup.LlamaLoadingModel", pct, (int)pct);
                Thread.Sleep(500);
            }

            if (attemptIndex + 1 < gpuLayerAttempts.Count)
            {
                StopLlamaProcesses();
                continue;
            }

            NativeLog.WriteKey("Startup.LlamaTimeout", null, waitSec);
            TailLog(logFile);
            return 1;
        }

        return 1;
    }

    private static void SetGpuLayers(List<string> args, int gpuLayers)
    {
        var index = args.FindIndex(a => string.Equals(a, "-ngl", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Count)
        {
            args[index + 1] = gpuLayers.ToString();
            return;
        }

        args.AddRange(["-ngl", gpuLayers.ToString()]);
    }

    private static void StartLlamaProcess(string exe, List<string> args, string workDir, string logFile)
    {
        try { if (File.Exists(logFile)) File.Delete(logFile); } catch { /* ignore */ }
        var errLog = logFile + ".err";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = workDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            var proc = Process.Start(psi);
            if (proc is null)
                throw new InvalidOperationException("Process.Start returned null");

            _ = Task.Run(async () =>
            {
                try
                {
                    var lines = await proc.StandardOutput.ReadToEndAsync();
                    await File.WriteAllTextAsync(logFile, lines);
                }
                catch { /* ignore */ }
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(err))
                        await File.AppendAllTextAsync(errLog, err);
                }
                catch { /* ignore */ }
            });
            return;
        }
        catch
        {
            /* fallback */
        }

        var joined = string.Join(" ", args.Select(Quote));
        var fallback = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = joined,
            WorkingDirectory = workDir,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        Process.Start(fallback);
    }

    private static string Quote(string arg) =>
        arg.Contains(' ') || arg.Contains('"') ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;

    /// <param name="waitAfterKill">再起動前は true（ポート解放待ち）。アプリ終了時は false で UI をブロックしない。</param>
    internal static void StopLlamaProcesses(bool waitAfterKill = true)
    {
        foreach (var proc in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
            finally
            {
                proc.Dispose();
            }
        }

        if (waitAfterKill)
            Thread.Sleep(2000);
    }

    private static void SetManagedFlag(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, "1");
        LlamaManagedMarker.RemoveLegacyMarkers(dir!);
    }

    private static int ReadIntMarker(string path)
    {
        if (!File.Exists(path))
            return 0;
        return int.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : 0;
    }

    private static string ReadStringMarker(string path) =>
        File.Exists(path) ? File.ReadAllText(path).Trim() : "";

    private static void WriteMarker(string path, string value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, value);
    }

    private static void TailLog(string logFile)
    {
        if (!File.Exists(logFile))
            return;
        try
        {
            var lines = File.ReadAllLines(logFile);
            foreach (var line in lines.TakeLast(15))
                NativeLog.Write(line);
        }
        catch { /* ignore */ }
    }
}
