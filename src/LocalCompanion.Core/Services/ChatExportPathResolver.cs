using System.Text;
using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット書き出しの保存先フォルダーを解決する。</summary>
internal static class ChatExportPathResolver
{
    private static readonly string[] BlockedPathPrefixes =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
    ];

    public static ChatExportPathResolution Resolve(ChatExportTarget target, string? configuredDataDirectory = null)
    {
        return target.Kind switch
        {
            ChatExportTargetKind.Desktop => ResolveDesktop(),
            ChatExportTargetKind.Directory => ResolveCustomDirectory(target.DirectoryPath),
            ChatExportTargetKind.Documents => ResolveSpecialFolder(
                Environment.SpecialFolder.MyDocuments,
                "Chat.Export.Error.Documents"),
            ChatExportTargetKind.Downloads => ResolveDownloads(),
            ChatExportTargetKind.UserData => ResolveKnownDirectory(
                AppPaths.ResolveUserDataDirectory(configuredDataDirectory),
                usedFallback: false),
            // exe 横への書き込みは拒否し、ユーザーデータの exports へ逃がす
            ChatExportTargetKind.AppRoot => ResolveKnownDirectory(
                Path.Combine(AppPaths.ResolveUserDataDirectory(configuredDataDirectory), "exports"),
                usedFallback: true),
            ChatExportTargetKind.RemovableStorage => ResolveRemovableStorage(),
            _ => ChatExportPathResolution.Fail(
                LocalizationService.Instance.Get("Chat.Export.Error.Destination")),
        };
    }

    public static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".lc-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok", new UTF8Encoding(false));
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ChatExportPathResolution ResolveDesktop()
    {
        foreach (var candidate in EnumerateDesktopCandidates())
        {
            if (CanWriteToDirectory(candidate))
                return ChatExportPathResolution.Ok(candidate, usedFallback: false);
        }

        var fallback = Path.Combine(AppPaths.ResolveUserDataDirectory(null), "exports");
        Directory.CreateDirectory(fallback);
        if (CanWriteToDirectory(fallback))
            return ChatExportPathResolution.Ok(fallback, usedFallback: true);

        return ChatExportPathResolution.Fail(
            LocalizationService.Instance.Get("Chat.Export.Error.Destination"));
    }

    private static IEnumerable<string> EnumerateDesktopCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                 })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static ChatExportPathResolution ResolveCustomDirectory(string? rawPath)
    {
        var loc = LocalizationService.Instance;
        if (string.IsNullOrWhiteSpace(rawPath))
            return ChatExportPathResolution.Fail(loc.Get("Chat.Export.Error.PathInvalid"));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath.Trim().TrimEnd('"', '\'', '」', '』', '。', '.', '!', '?', '！', '？'));
        }
        catch
        {
            return ChatExportPathResolution.Fail(loc.Get("Chat.Export.Error.PathInvalid"));
        }

        if (IsDriveRoot(fullPath) || IsBlockedPath(fullPath))
            return ChatExportPathResolution.Fail(FormatPathDenied(fullPath));

        if (File.Exists(fullPath))
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;

        if (!Directory.Exists(fullPath))
        {
            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                return ChatExportPathResolution.Fail(ex.Message);
            }
        }

        if (!CanWriteToDirectory(fullPath))
            return ChatExportPathResolution.Fail(FormatPathDenied(fullPath));

        return ChatExportPathResolution.Ok(fullPath, usedFallback: false);
    }

    private static string FormatPathDenied(string fullPath)
    {
        var loc = LocalizationService.Instance;
        return loc is null
            ? $"Path denied: {fullPath}"
            : loc.Format("Chat.Export.Error.PathDenied", fullPath);
    }

    private static ChatExportPathResolution ResolveSpecialFolder(
        Environment.SpecialFolder folder,
        string errorKey)
    {
        var path = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(path))
            return ChatExportPathResolution.Fail(LocalizationService.Instance.Get(errorKey));

        return ResolveKnownDirectory(Path.GetFullPath(path), usedFallback: false);
    }

    private static ChatExportPathResolution ResolveDownloads()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var downloads = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloads) && CanWriteToDirectory(downloads))
                return ChatExportPathResolution.Ok(downloads, usedFallback: false);
        }

        return ResolveSpecialFolder(Environment.SpecialFolder.MyDocuments, "Chat.Export.Error.Downloads");
    }

    private static ChatExportPathResolution ResolveKnownDirectory(string directory, bool usedFallback)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return ChatExportPathResolution.Fail(LocalizationService.Instance.Get("Chat.Export.Error.Destination"));

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return ChatExportPathResolution.Fail(ex.Message);
        }

        if (!CanWriteToDirectory(directory))
            return ChatExportPathResolution.Fail(
                LocalizationService.Instance.Format("Chat.Export.Error.PathDenied", directory));

        return ChatExportPathResolution.Ok(directory, usedFallback: usedFallback);
    }

    private static ChatExportPathResolution ResolveRemovableStorage()
    {
        var loc = LocalizationService.Instance;
        var drives = GetReadyRemovableDrives();
        if (drives.Count == 0)
            return ChatExportPathResolution.Fail(loc.Get("Chat.Export.Error.UsbNotFound"));

        if (drives.Count == 1)
            return ResolveKnownDirectory(drives[0].RootDirectory, usedFallback: false);

        var listing = string.Join(
            Environment.NewLine,
            drives.Select(d => $"- {d.RootDirectory} ({d.Label})"));
        return ChatExportPathResolution.Fail(loc.Format("Chat.Export.Error.UsbAmbiguous", Environment.NewLine, listing));
    }

    internal static IReadOnlyList<RemovableDriveCandidate> GetReadyRemovableDrives()
    {
        var results = new List<RemovableDriveCandidate>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Removable)
                continue;

            try
            {
                if (!drive.IsReady)
                    continue;

                var root = drive.RootDirectory.FullName.TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.DriveFormat
                    : drive.VolumeLabel.Trim();
                results.Add(new RemovableDriveCandidate(root, label));
            }
            catch
            {
                /* ignore inaccessible drives */
            }
        }

        return results;
    }

    private static bool IsBlockedPath(string fullPath)
    {
        foreach (var blocked in BlockedPathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(blocked))
                continue;

            var blockedFull = Path.GetFullPath(blocked).TrimEnd('\\');
            if (fullPath.Equals(blockedFull, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(blockedFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ドライブ根（例: C:\）への書き出しは拒否する。</summary>
    internal static bool IsDriveRoot(string fullPath)
    {
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
                return false;
            var normalized = Path.GetFullPath(fullPath).TrimEnd('\\');
            var rootNorm = Path.GetFullPath(root).TrimEnd('\\');
            return normalized.Equals(rootNorm, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal sealed record RemovableDriveCandidate(string RootDirectory, string Label);
}

internal sealed record ChatExportPathResolution(
    bool Success,
    string? Directory,
    bool UsedFallback,
    string? ErrorMessage)
{
    public static ChatExportPathResolution Ok(string directory, bool usedFallback) =>
        new(true, directory, usedFallback, null);

    public static ChatExportPathResolution Fail(string errorMessage) =>
        new(false, null, false, errorMessage);
}
