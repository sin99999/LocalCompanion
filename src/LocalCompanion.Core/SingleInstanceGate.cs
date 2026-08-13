using System.Globalization;
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
            // Mutex が使えない環境では起動を優先（終了時の Job Object 等で緩和）
            StartupLog.Write($"SingleInstanceGate fail-open: {ex.Message}");
            return true;
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
                if (string.Equals(text, "en", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "english", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.English;

                if (string.Equals(text, "ja", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "japanese", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.Japanese;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"SingleInstanceGate language peek failed: {ex.Message}");
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.English
            : AppLanguage.Japanese;
    }

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
