using LocalCompanion.Models;
using LocalCompanion.Localization;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

/// <summary>Windows 上の VOICEVOX / ENGINE 実行ファイルを探す（見つからなければ null）。</summary>
public sealed class VoicevoxInstallLocator
{
    private readonly VoicevoxOptions _opt;
    private string? _cachedLauncher;

    public VoicevoxInstallLocator(IOptions<VoicevoxOptions> opt) => _opt = opt.Value;

    public bool IsInstalled => FindLauncher() is not null;

    public VoicevoxInstallDto DescribeInstall()
    {
        var path = FindLauncher();
        if (path is not null)
            return new VoicevoxInstallDto(true, path, null);

        var loc = LocalizationService.Instance;
        var hint = string.IsNullOrWhiteSpace(_opt.EngineExePath)
            ? loc.Get("Voicevox.Install.NotFound")
            : loc.Format("Voicevox.Install.ConfiguredPathNotFound", _opt.EngineExePath);
        return new VoicevoxInstallDto(false, null, hint);
    }

    public void InvalidateCache() => _cachedLauncher = null;

    /// <summary>実行中プロセスの照合に使う VOICEVOX インストール先ディレクトリ。</summary>
    public IReadOnlyList<string> GetInstallRootPaths()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                roots.Add(Path.GetFullPath(path));
            }
            catch
            {
                /* ignore */
            }
        }

        var launcher = FindLauncher();
        if (launcher is not null)
        {
            AddRoot(Path.GetDirectoryName(launcher));
            var parent = Path.GetDirectoryName(launcher);
            if (parent is not null)
                AddRoot(Path.GetDirectoryName(parent));
        }

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        AddRoot(Path.Combine(localPrograms, "VOICEVOX"));
        AddRoot(Path.Combine(localPrograms, "VOICEVOX ENGINE"));

        foreach (var voicevoxDir in EnumerateVoicevoxInstallDirectories())
            AddRoot(voicevoxDir);

        if (!string.IsNullOrWhiteSpace(_opt.EngineExePath))
        {
            try
            {
                AddRoot(Path.GetDirectoryName(Path.GetFullPath(_opt.EngineExePath)));
            }
            catch
            {
                /* ignore */
            }
        }

        return roots.ToList();
    }

    public string? FindLauncher()
    {
        if (_cachedLauncher is not null && File.Exists(_cachedLauncher))
            return _cachedLauncher;

        if (!string.IsNullOrWhiteSpace(_opt.EngineExePath))
        {
            var configured = Path.GetFullPath(_opt.EngineExePath);
            if (File.Exists(configured))
                return _cachedLauncher = configured;
        }

        var ordered = new List<string>();

        ordered.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "VOICEVOX ENGINE",
            "VOICEVOX ENGINE.exe"));

        foreach (var voicevoxDir in EnumerateVoicevoxInstallDirectories())
        {
            if (!Directory.Exists(voicevoxDir))
                continue;

            ordered.AddRange(FindEngineRunCandidates(voicevoxDir));
            ordered.Add(Path.Combine(voicevoxDir, "VOICEVOX.exe"));
        }

        foreach (var path in ordered.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                return _cachedLauncher = path;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateVoicevoxInstallDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Yield(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            return seen.Add(path);
        }

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        if (Yield(Path.Combine(localPrograms, "VOICEVOX")))
            yield return Path.Combine(localPrograms, "VOICEVOX");

        var defaultPf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Yield(Path.Combine(defaultPf, "VOICEVOX")))
            yield return Path.Combine(defaultPf, "VOICEVOX");

        var defaultPfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (Yield(Path.Combine(defaultPfX86, "VOICEVOX")))
            yield return Path.Combine(defaultPfX86, "VOICEVOX");

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                continue;

            var pf = Path.Combine(drive.Name, "Program Files", "VOICEVOX");
            if (Yield(pf))
                yield return pf;

            var pfX86 = Path.Combine(drive.Name, "Program Files (x86)", "VOICEVOX");
            if (Yield(pfX86))
                yield return pfX86;
        }
    }

    private static IEnumerable<string> FindEngineRunCandidates(string root)
    {
        var direct = new[]
        {
            Path.Combine(root, "resources", "vv-engine", "run.exe"),
            Path.Combine(root, "vv-engine", "run.exe"),
            Path.Combine(root, "resources", "engine", "run.exe"),
        };
        foreach (var p in direct)
            yield return p;

        List<string>? deep = null;
        try
        {
            deep = Directory
                .EnumerateFiles(root, "run.exe", SearchOption.AllDirectories)
                .Where(file =>
                    file.Contains("vv-engine", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains("\\engine\\", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            /* ignore */
        }

        if (deep is not null)
        {
            foreach (var file in deep)
                yield return file;
        }
    }
}
