using System.IO.Compression;

namespace LocalCompanion.Services;

/// <summary>会話・RAG・設定が入ったユーザーデータフォルダーを ZIP に書き出す。</summary>
public static class UserDataBackup
{
    /// <summary>
    /// dataDirectory の中身を destinationZipPath に書き出す。
    /// SQLite 等が開いているファイルも読み取り共有でコピーする。
    /// 既存 ZIP は一時ファイル作成成功後に置き換える（失敗時に旧 ZIP を消さない）。
    /// </summary>
    public static int ExportToZip(string dataDirectory, string destinationZipPath, Action<string>? beforeCopyFile = null)
    {
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException(dataDirectory);

        var destFull = Path.GetFullPath(destinationZipPath);
        var destDir = Path.GetDirectoryName(destFull);
        if (string.IsNullOrWhiteSpace(destDir))
            throw new ArgumentException("Invalid destination path.", nameof(destinationZipPath));

        Directory.CreateDirectory(destDir);
        var tempZip = Path.Combine(
            destDir,
            Path.GetFileName(destFull) + "." + Guid.NewGuid().ToString("N") + ".partial.zip");

        try
        {
            var count = 0;
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories))
                {
                    var fileFull = Path.GetFullPath(file);
                    if (string.Equals(fileFull, destFull, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileFull, tempZip, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

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
            }

            if (File.Exists(destFull))
                File.Replace(tempZip, destFull, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempZip, destFull);

            return count;
        }
        catch
        {
            try
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
            catch
            {
                /* ignore */
            }

            throw;
        }
    }
}
