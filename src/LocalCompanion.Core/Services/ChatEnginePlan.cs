using LocalCompanion.Models;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Services;

/// <summary>
/// llama.cpp と Strata のどちらを起動するか。
/// モデル取得を許すまで Strata は立てず、今の llama-server も止めない。
/// </summary>
public sealed class ChatEnginePlan
{
    /// <summary>約 70GB の取得は、許可が出るまで始めない。</summary>
    public const bool AllowModelDownload = false;

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

    public static ChatEnginePlan Decide(string? savedEngine, bool strataCommandExists, bool allowModelDownload)
    {
        var engine = InferenceEngineChoices.Normalize(savedEngine);
        if (engine != InferenceEngineChoices.Strata)
            return Llama();

        if (!strataCommandExists || !allowModelDownload)
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
            strataArguments: LaunchArguments());
    }

    public static IReadOnlyList<string> LaunchArguments() =>
    [
        "launch",
        "--model", "iq2_xs",
        "--yes",
        "--no-tui",
        "--access", "local",
        "--port", ListenPort.ToString(),
    ];

    public static IReadOnlyList<string> StopArguments() => ["launch", "--stop"];

    public static ChatEnginePlan ForInstalled(string appRoot)
    {
        try
        {
            var dataDir = AppPaths.ResolveUserDataDirectory(LlamaInstallConfig.Load(appRoot).DataDirectory);
            var saved = AppSettingsStore.LoadOrDefault(dataDir).InferenceEngine;
            return Decide(saved, FindExecutable() != null, AllowModelDownload);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Chat engine setting unreadable; staying on llama.cpp");
            return Decide(null, strataCommandExists: false, allowModelDownload: false);
        }
    }

    /// <summary>PATH 上の strata.exe だけ。シェル経由では探さない。</summary>
    public static string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "strata.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
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
