using System.Text.Json;
using System.Windows.Forms;
using LocalCompanion.Localization;

namespace LocalCompanion;

/// <summary>同一マシンで LocalCompanion を二重起動しない。</summary>
public static class SingleInstanceGate
{
    private static Mutex? _mutex;

    /// <summary>このプロセスが唯一のインスタンスなら true。2 つ目以降は false。</summary>
    public static bool TryEnter()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: @"Local\LocalCompanion_WinUI_SingleInstance", out var createdNew);
            if (createdNew)
                return true;

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            // Mutex が使えないときは、二重 llama を避けるためファイルロックへ倒す。
            StartupLog.Write($"SingleInstanceGate mutex failed, file lock: {ex.Message}");
            return TryAcquireCanonicalExclusiveFileLock();
        }
    }

    /// <summary>2 つ目の起動を無言終了にしない。LocalizationService 初期化前でも使える。</summary>
    public static void NotifyAlreadyRunning()
    {
        var language = PeekSavedLanguage();
        try
        {
            MessageBox.Show(
                GetAlreadyRunningMessage(language),
                GetAlreadyRunningTitle(language),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"SingleInstanceGate notify failed: {ex.Message}");
        }
    }

    internal static string GetAlreadyRunningMessage(AppLanguage language) =>
        ReadResource(language, "App.AlreadyRunning",
            "LocalCompanion はすでに起動しています。\n先に開いているウィンドウをご利用ください。");

    internal static string GetAlreadyRunningTitle(AppLanguage language) =>
        ReadResource(language, "App.AlreadyRunning.Title", "LocalCompanion");

    internal static AppLanguage PeekSavedLanguage()
    {
        try
        {
            foreach (var dir in EnumerateLanguageSettingDirectories())
            {
                var path = Path.Combine(dir, "language-settings.json");
                if (!File.Exists(path))
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("language", out var lang))
                    continue;

                var text = lang.GetString();
                if (AppLanguages.TryParse(text, out var parsed))
                    return parsed;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"SingleInstanceGate language peek failed: {ex.Message}");
        }

        return AppLanguages.FromUiCulture();
    }

    private static FileStream? _fallbackLock;

    internal static string ResolveFallbackLockPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalCompanionLlama",
            ".localcompanion-single-instance.lock");

    /// <summary>同じパスを 2 回取れなければ false（他インスタンスが保持中）。</summary>
    internal static bool TryAcquireExclusiveFileLock(string lockFilePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var stream = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            _fallbackLock = stream;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // 想定外の失敗で「既に起動」誤表示→起動不能にしない（二重起動より起動優先）
            StartupLog.Write($"SingleInstanceGate file lock unexpected, allow start: {ex.Message}");
            return true;
        }
    }

    private static bool TryAcquireCanonicalExclusiveFileLock() =>
        TryAcquireExclusiveFileLock(ResolveFallbackLockPath());

    private static IEnumerable<string> EnumerateLanguageSettingDirectories()
    {
        yield return Path.Combine(AppPaths.Current.Root, "data");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalCompanionLlama");
    }

    private static string ReadResource(AppLanguage language, string key, string fallback)
    {
        var table = LocalizationResources.For(language);
        return table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
