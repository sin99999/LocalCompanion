using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>チャット用に URL から本文テキストを1回取得する（RAG 登録はしない）。</summary>
public static class ChatUrlContentFetcher
{
    public const int MaxDownloadBytes = 2 * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanion/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain,application/json,text/markdown,*/*;q=0.8");
        return client;
    }

    public static async Task<(string DisplayName, string Text)> FetchAsync(string urlInput, CancellationToken ct = default)
    {
        var uri = ParseHttpUrl(urlInput);
        var displayName = BuildDisplayName(uri);

        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LocalizedServiceException("Chat.Url.NetworkError");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new LocalizedServiceException("Chat.Url.DownloadFailed", (int)response.StatusCode);

            var bytes = await ReadLimitedBytesAsync(response, ct);
            var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet, bytes);
            var raw = encoding.GetString(bytes).Trim();
            if (raw.Length == 0)
                throw new LocalizedServiceException("Chat.Url.Empty");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var text = ExtractText(raw, mediaType, uri);
            if (string.IsNullOrWhiteSpace(text))
                throw new LocalizedServiceException("Chat.Url.Empty");

            return (displayName, text);
        }
    }

    private static Uri ParseHttpUrl(string urlInput)
    {
        var trimmed = urlInput.Trim();
        if (trimmed.Length == 0)
            throw new LocalizedServiceException("Chat.Url.Invalid");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new LocalizedServiceException("Chat.Url.Invalid");
        }

        if (uri.Scheme is not ("http" or "https"))
            throw new LocalizedServiceException("Chat.Url.SchemeNotAllowed");

        return uri;
    }

    private static string BuildDisplayName(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path is "" or "/")
            path = string.Empty;

        var label = uri.Host + path;
        if (!string.IsNullOrEmpty(uri.Query))
            label += uri.Query;

        return label.Length <= 120 ? label : label[..117] + "...";
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0)
                break;

            if (buffer.Length + read > MaxDownloadBytes)
                throw new LocalizedServiceException("Chat.Url.TooLarge", MaxDownloadBytes / (1024 * 1024));

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Encoding ResolveEncoding(string? charsetName, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(charsetName))
        {
            try
            {
                return Encoding.GetEncoding(charsetName);
            }
            catch (ArgumentException)
            {
                /* fall through */
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        return Encoding.UTF8;
    }

    private static string ExtractText(string raw, string mediaType, Uri uri)
    {
        if (IsHtml(mediaType, raw))
        {
            var title = ExtractHtmlTitle(raw);
            var body = RagDocumentReader.ExtractPlainTextFromHtml(raw);
            if (!string.IsNullOrWhiteSpace(title))
                return $"URL: {uri}\nTitle: {title}\n\n{body}";
            return $"URL: {uri}\n\n{body}";
        }

        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(mediaType))
        {
            return $"URL: {uri}\n\n{raw}";
        }

        throw new LocalizedServiceException("Chat.Url.UnsupportedContent");
    }

    private static bool IsHtml(string mediaType, string raw)
    {
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return true;

        var probe = raw.Length > 512 ? raw[..512] : raw;
        return Regex.IsMatch(probe, @"<!DOCTYPE\s+html|<html\b", RegexOptions.IgnoreCase);
    }

    private static string? ExtractHtmlTitle(string html)
    {
        var match = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return null;

        var title = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title.Length > 0 ? title : null;
    }
}
