using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Services;

/// <summary>
/// llama.cpp と Strata のどちらを起動するか。
/// モデル取得を許すまで Strata は立てず、今の llama-server も止めない。
/// </summary>
public sealed class ChatEnginePlan
{
    /// <summary>モデル取得は許可済み。データフォルダを指定しているときは、大きな GGUF が来るまで llama.cpp のまま。</summary>
    public const bool AllowModelDownload = true;

    public const string ListenHost = "127.0.0.1";

    public const int ListenPort = 8080;

    public string Engine { get; }

    public bool StopManagedLlama { get; }

    public bool StartLlama { get; }

    public bool StartStrata { get; }

    public bool HoldStrataUntilDownload { get; }

    public IReadOnlyList<string> StrataArguments { get; }

    private ChatEnginePlan(
        string engine,
        bool stopManagedLlama,
        bool startLlama,
        bool startStrata,
        bool holdStrataUntilDownload,
        IReadOnlyList<string> strataArguments)
    {
        Engine = engine;
        StopManagedLlama = stopManagedLlama;
        StartLlama = startLlama;
        StartStrata = startStrata;
        HoldStrataUntilDownload = holdStrataUntilDownload;
        StrataArguments = strataArguments;
    }

    public static ChatEnginePlan Decide(
        string? savedEngine,
        bool strataCommandExists,
        bool allowModelDownload,
        string? dataDirectory = null,
        bool modelFilesReady = true)
    {
        var engine = InferenceEngineChoices.Normalize(savedEngine);
        if (engine != InferenceEngineChoices.Strata)
            return Llama();

        var hasDataDirectory = TryNormalizeDataDirectory(dataDirectory, out var dataDir);
        var dataDirectoryRejected = !string.IsNullOrWhiteSpace(dataDirectory) && !hasDataDirectory;
        if (!strataCommandExists || !allowModelDownload || dataDirectoryRejected || (hasDataDirectory && !modelFilesReady))
        {
            return new ChatEnginePlan(
                InferenceEngineChoices.Strata,
                stopManagedLlama: false,
                startLlama: true,
                startStrata: false,
                holdStrataUntilDownload: true,
                strataArguments: []);
        }

        return new ChatEnginePlan(
            InferenceEngineChoices.Strata,
            stopManagedLlama: true,
            startLlama: false,
            startStrata: true,
            holdStrataUntilDownload: false,
            strataArguments: LaunchArguments(hasDataDirectory ? dataDir : null));
    }

    public static IReadOnlyList<string> LaunchArguments(string? dataDirectory = null)
    {
        var args = new List<string>
        {
            "launch",
            "--model", "iq2_xs",
            "--context", "64k",
            "--vision", "cpu",
            "--tune", "skip",
            "--yes",
            "--no-tui",
            "--access", "local",
            "--port", ListenPort.ToString(),
        };
        if (TryNormalizeDataDirectory(dataDirectory, out var dataDir))
        {
            args.Add("--data-dir");
            args.Add(dataDir);
        }

        return args;
    }

    /// <summary>ルート付きのフォルダだけ通す。旗や引用符は渡さない。</summary>
    public static bool TryNormalizeDataDirectory(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.Contains('"') || trimmed.Contains('\'') || trimmed.Contains("--", StringComparison.Ordinal))
            return false;
        if (trimmed.StartsWith('-'))
            return false;
        if (!Path.IsPathRooted(trimmed))
            return false;

        normalized = trimmed;
        return true;
    }

    public static bool HasLargeGguf(string dataDirectory, long minimumBytes = 1_000_000_000)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            return false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 4,
        };
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.gguf", options))
        {
            try
            {
                if (new FileInfo(file).Length >= minimumBytes)
                    return true;
            }
            catch (IOException)
            {
                /* 取得中のファイルは次の起動で見る */
            }
        }

        return false;
    }

    public static IReadOnlyList<string> StopArguments() => ["launch", "--stop"];

    public static ChatEnginePlan ForInstalled(string appRoot)
    {
        try
        {
            var dataDir = AppPaths.ResolveUserDataDirectory(LlamaInstallConfig.Load(appRoot).DataDirectory);
            var saved = AppSettingsStore.LoadOrDefault(dataDir);
            var filesReady = !TryNormalizeDataDirectory(saved.StrataDataDirectory, out var modelDir)
                || HasLargeGguf(modelDir);
            return Decide(
                saved.InferenceEngine,
                FindExecutable() != null,
                AllowModelDownload,
                saved.StrataDataDirectory,
                filesReady);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Chat engine setting unreadable; staying on llama.cpp");
            return Decide(null, strataCommandExists: false, allowModelDownload: false);
        }
    }

    /// <summary>PATH か、ユーザーフォルダの Terminal。シェル経由では探さない。</summary>
    public static string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim();
            var exe = Path.Combine(trimmed, "strata.exe");
            if (File.Exists(exe))
                return exe;
            var cmd = Path.Combine(trimmed, "strata.cmd");
            if (File.Exists(cmd))
                return cmd;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            var bin = Path.Combine(local, "Strata", "terminal", "current", "bin");
            var exe = Path.Combine(bin, "strata.exe");
            if (File.Exists(exe))
                return exe;
            var cmd = Path.Combine(bin, "strata.cmd");
            if (File.Exists(cmd))
                return cmd;
        }

        return null;
    }

    public static void RevertSavedEngineToLlama(string appRoot)
    {
        try
        {
            var dataDir = AppPaths.ResolveUserDataDirectory(LlamaInstallConfig.Load(appRoot).DataDirectory);
            AppSettingsStore.SetInferenceEngine(dataDir, InferenceEngineChoices.LlamaCpp);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Could not set the chat engine back to llama.cpp");
        }
    }

    private static ChatEnginePlan Llama() =>
        new(
            InferenceEngineChoices.LlamaCpp,
            stopManagedLlama: false,
            startLlama: true,
            startStrata: false,
            holdStrataUntilDownload: false,
            strataArguments: []);
}
