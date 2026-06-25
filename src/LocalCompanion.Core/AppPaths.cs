namespace LocalCompanion;

/// <summary>配布フォルダのルート（models / scripts と並ぶ階層）を解決する。</summary>
public sealed class AppPaths
{
    public static AppPaths Current { get; private set; } = ResolveDefault();

    public string Root { get; }
    public string ScriptsDirectory { get; }
    public string ModelsDirectory { get; }
    public string ToolsDirectory { get; }
    public string ContentRoot { get; }

    private AppPaths(string root, string contentRoot)
    {
        Root = root;
        ContentRoot = contentRoot;
        ScriptsDirectory = Path.Combine(root, "scripts");
        ModelsDirectory = Path.Combine(root, "models");
        ToolsDirectory = Path.Combine(root, "tools", "llama-cpp");
    }

    public static void Initialize(string? contentRoot = null)
    {
        var installDir = GetInstallDirectory();
        var runtimeDir = AppContext.BaseDirectory;
        contentRoot ??= ResolveContentRoot(installDir, runtimeDir);
        var root = FindDistributionRoot(installDir, contentRoot);
        Current = new AppPaths(root, contentRoot);
    }

    private static AppPaths ResolveDefault()
    {
        var installDir = GetInstallDirectory();
        var runtimeDir = AppContext.BaseDirectory;
        var contentRoot = ResolveContentRoot(installDir, runtimeDir);
        return new(FindDistributionRoot(installDir, contentRoot), contentRoot);
    }

    /// <summary>ユーザーが置いた exe のフォルダ（単一ファイル publish では BaseDirectory は Temp になる）。</summary>
    public static string GetInstallDirectory()
    {
        foreach (var candidate in EnumerateInstallDirectoryCandidates())
        {
            if (Directory.Exists(Path.Combine(candidate, "scripts")))
                return candidate;
        }

        foreach (var candidate in EnumerateInstallDirectoryCandidates())
        {
            if (!IsSingleFileExtractDirectory(candidate))
                return candidate;
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static IEnumerable<string> EnumerateInstallDirectoryCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void TryAdd(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (string.IsNullOrEmpty(dir))
                    return;
                var full = Path.GetFullPath(dir);
                if (seen.Add(full))
                    list.Add(full);
            }
            catch
            {
                /* ignore bad paths */
            }
        }

        TryAdd(Environment.ProcessPath);

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 0)
            TryAdd(args[0]);

        try
        {
            TryAdd(Directory.GetCurrentDirectory());
        }
        catch
        {
            /* ignore */
        }

        return list;
    }

    private static bool IsSingleFileExtractDirectory(string directory) =>
        directory.Contains($"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || directory.Contains($"{Path.AltDirectorySeparatorChar}.net{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>appsettings.json がある publish 出力フォルダ。</summary>
    public static string ResolveContentRoot(string installDirectory, string? runtimeDirectory = null)
    {
        runtimeDirectory ??= AppContext.BaseDirectory;
        var install = Path.GetFullPath(installDirectory);
        var runtime = Path.GetFullPath(runtimeDirectory);

        if (File.Exists(Path.Combine(install, "appsettings.json")))
            return install;

        if (File.Exists(Path.Combine(runtime, "appsettings.json")))
            return runtime;

        return install;
    }

    /// <summary>exe 配置・dotnet run・publish 出力のいずれでも配布ルートを探す。</summary>
    public static string FindDistributionRoot(string installDirectory, string? contentRoot = null)
    {
        var configured = TryGetConfiguredDistributionRoot();
        if (configured is not null)
            return configured;

        contentRoot ??= ResolveContentRoot(installDirectory, AppContext.BaseDirectory);

        // publish 出力 / ZIP 解凍フォルダ（scripts + models が exe と同階層）を最優先
        if (IsCompleteDistributionFolder(contentRoot))
            return Path.GetFullPath(contentRoot);

        if (IsCompleteDistributionFolder(installDirectory))
            return Path.GetFullPath(installDirectory);

        var dir = new DirectoryInfo(Path.GetFullPath(installDirectory));
        string? distributionRoot = null;
        while (dir is not null)
        {
            if (IsCompleteDistributionFolder(dir.FullName))
            {
                if (dir.Name.Equals("AppX", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = dir.Parent;
                    if (parent is not null && IsCompleteDistributionFolder(parent.FullName))
                        dir = parent;
                }

                distributionRoot = dir.FullName;

                // dotnet build / winapp 開発時のみ: bin/obj から起動したらリポジトリ直下を使う
                if (File.Exists(Path.Combine(dir.FullName, "LocalCompanion.csproj"))
                    && IsDevelopmentOutputPath(installDirectory))
                {
                    return dir.FullName;
                }
            }

            dir = dir.Parent;
        }

        if (distributionRoot is not null)
            return distributionRoot;

        dir = new DirectoryInfo(Path.GetFullPath(installDirectory));
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "scripts")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(installDirectory);
    }

    private static bool IsCompleteDistributionFolder(string directory) =>
        Directory.Exists(Path.Combine(directory, "scripts"))
        && Directory.Exists(Path.Combine(directory, "models"));

    private static bool IsDevelopmentOutputPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetConfiguredDistributionRoot()
    {
        var env = Environment.GetEnvironmentVariable("LC_DIST_ROOT");
        if (string.IsNullOrWhiteSpace(env))
            return null;

        var full = Path.GetFullPath(env.Trim());
        return Directory.Exists(Path.Combine(full, "scripts")) ? full : null;
    }

    /// <summary>
    /// RAG・会話・言語設定などユーザーデータの保存先。
    /// 配布 ZIP / デスクトップ公開フォルダからの exe 直起動時は {Root}\data\（開発 bin 出力時は LocalAppData）。
    /// </summary>
    public static string ResolveUserDataDirectory(string? configuredDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataDirectory))
        {
            var trimmed = configuredDataDirectory.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(Current.Root, trimmed));
        }

        var root = Current.Root;
        if (IsCompleteDistributionFolder(root) && !IsDevelopmentOutputPath(GetInstallDirectory()))
            return Path.Combine(root, "data");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalCompanionLlama");
    }
}
