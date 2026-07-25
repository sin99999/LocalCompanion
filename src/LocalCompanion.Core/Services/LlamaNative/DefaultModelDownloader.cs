using System.Text.Json;

using LocalCompanion.Localization;

namespace LocalCompanion.Services.LlamaNative;

internal static class DefaultModelDownloader
{
    private const string DefaultFileName = LlamaDefaultModel.ChatFileName;
    private const string DefaultUrl = LlamaDefaultModel.ChatUrl;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(6) };

    internal enum BootstrapResult
    {
        /// <summary>モデルがある／取得不要／取得成功。</summary>
        Ready,

        /// <summary>既定モデルのダウンロードに失敗し、利用可能なチャット GGUF もない。</summary>
        Failed,
    }

    internal static BootstrapResult TryBootstrap(string root)
    {
        var modelsDir = Path.Combine(root, "models");
        var markerPath = Path.Combine(modelsDir, ".default-model-bootstrap.json");
        if (File.Exists(markerPath))
            return BootstrapResult.Ready;

        var dest = Path.Combine(modelsDir, DefaultFileName);
        if (File.Exists(dest))
        {
            WriteMarker(modelsDir, "already_present");
            return BootstrapResult.Ready;
        }

        if (CountChatGguf(modelsDir) > 0)
        {
            WriteMarker(modelsDir, "skipped_existing_models");
            NativeLog.WriteKey("Startup.DefaultModel.SkipExisting");
            return BootstrapResult.Ready;
        }

        var settings = LlamaInstallConfig.Load(root);
        var dataDir = AppPaths.ResolveUserDataDirectory(settings.DataDirectory);
        var additionalFolder = ModelLibrarySettings.LoadAdditionalFolder(dataDir);
        if (!string.IsNullOrWhiteSpace(additionalFolder) && CountChatGguf(additionalFolder) > 0)
        {
            WriteMarker(modelsDir, "skipped_external_folder");
            NativeLog.WriteKey("Startup.DefaultModel.SkipExisting");
            return BootstrapResult.Ready;
        }

        NativeLog.WriteKey("Startup.DefaultModel.Begin", null, DefaultFileName);
        try
        {
            Directory.CreateDirectory(modelsDir);
            using var response = Http.GetAsync(DefaultUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var fs = File.Create(dest);
            DownloadProgress.CopyStream(stream, fs, "Startup.Download.Label.DefaultModel", total);
            if (!File.Exists(dest) || new FileInfo(dest).Length < 1_000_000)
                throw new LocalizedServiceException("Startup.Error.DownloadInvalid");
            WriteMarker(modelsDir, "downloaded");
            NativeLog.WriteKey("Startup.DefaultModel.Done", null, DefaultFileName);
            return BootstrapResult.Ready;
        }
        catch (Exception ex)
        {
            NativeLog.WriteKey("Startup.DefaultModel.DownloadFailed", null, UserFacingErrorLocalizer.Localize(ex));
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
            return BootstrapResult.Failed;
        }
    }

    private static int CountChatGguf(string modelsDir)
    {
        if (!Directory.Exists(modelsDir))
            return 0;
        return Directory.EnumerateFiles(modelsDir, "*.gguf")
            .Count(f => !Path.GetFileName(f).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase));
    }

    internal static void MarkBootstrapSkipped(string modelsDir, string status) => WriteMarker(modelsDir, status);

    private static void WriteMarker(string modelsDir, string status)
    {
        var markerPath = Path.Combine(modelsDir, ".default-model-bootstrap.json");
        File.WriteAllText(markerPath, JsonSerializer.Serialize(new
        {
            status,
            fileName = DefaultFileName,
            at = DateTimeOffset.Now.ToString("o"),
        }));
    }
}
