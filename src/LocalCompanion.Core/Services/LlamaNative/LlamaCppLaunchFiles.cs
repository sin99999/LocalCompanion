namespace LocalCompanion.Services.LlamaNative;

/// <summary>展開直後の llama.cpp が起動できる最低ファイルを見る。</summary>
internal static class LlamaCppLaunchFiles
{
    /// <summary>新しい Windows ZIP の llama-server.exe は impl DLL を呼ぶ薄い exe。</summary>
    internal const long StubExeMaxBytes = 32 * 1024;

    internal static bool HasRequiredServerFiles(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var exe = Path.Combine(directory, "llama-server.exe");
        if (!File.Exists(exe))
            return false;

        if (new FileInfo(exe).Length > StubExeMaxBytes)
            return true;

        return File.Exists(Path.Combine(directory, "llama-server-impl.dll"));
    }

    /// <summary>NTSTATUS の失敗（0xC0000135 欠 DLL、0xC0000906 起動不能など）。</summary>
    internal static bool IsWindowsLoaderFailure(int exitCode) =>
        unchecked((uint)exitCode) >= 0xC0000000u;
}
