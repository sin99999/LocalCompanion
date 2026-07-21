using Microsoft.Web.WebView2.Core;

namespace LocalCompanion.Services;

/// <summary>Evergreen WebView2 Runtime の有無（会話画面の前提）。</summary>
public static class WebView2RuntimeAvailability
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>Runtime が入っていれば true。API 失敗時は false。</summary>
    public static bool IsInstalled()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }
}
