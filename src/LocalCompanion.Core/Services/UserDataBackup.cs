using System.IO.Compression;

namespace LocalCompanion.Services;

/// <summary>会話・RAG・設定が入ったユーザーデータフォルダーを ZIP に書き出す。</summary>
public static class UserDataBackup
{
    /// <summary>
    /// dataDirectory の中身を destinationZipPath に書き出す。
    /// SQLite 等が開いているファイルも読み取り共有でコピーする。
    /// </summary>
    public static int ExportToZip(string dataDirectory, string destinationZipPath, Action<string>? beforeCopyFile = null)
    {
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException(dataDirectory);

        var destFull = Path.GetFullPath(destinationZipPath);
        if (File.Exists(destFull))
            File.Delete(destFull);

        var count = 0;
        using var zip = ZipFile.Open(destFull, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), destFull, StringComparison.OrdinalIgnoreCase))
                continue;

            beforeCopyFile?.Invoke(file);

            var relative = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
            var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(file);

            using var source = new FileStream(
                file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var target = entry.Open();
            source.CopyTo(target);
            count++;
        }

        return count;
    }
}
