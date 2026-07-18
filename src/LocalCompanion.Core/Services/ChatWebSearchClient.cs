using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

public sealed record ChatWebSearchHit(string Title, string Url, string Snippet);

/// <summary>キー不要の HTML 検索結果をパース／取得する（既定: DuckDuckGo HTML）。</summary>
internal static class ChatWebSearchClient
{
    public const int DefaultTopK = 3;
    private const int MaxDownloadBytes = 512 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex ResultBlock = new(
        @"class=""result__a""[^>]*href=""(?<href>[^""]+)""[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SnippetBlock = new(
        @"class=""result__snippet""[^>]*>(?<snippet>.*?)</(?:a|td|div)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalCompanion/1.0");
        return client;
    }

    public static async Task<IReadOnlyList<ChatWebSearchHit>> SearchAsync(
        string query,
        string? baseUrl,
        int topK,
        CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 2)
            return Array.Empty<ChatWebSearchHit>();

        topK = Math.Clamp(topK, 1, 8);
        var endpoint = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://html.duckduckgo.com/html/"
            : baseUrl.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme is not ("http" or "https"))
            throw new LocalizedServiceException("Chat.WebSearch.InvalidEndpoint");

        if (ChatUrlHostGuard.IsBlocked(endpointUri))
            throw new LocalizedServiceException("Chat.Url.HostNotAllowed");

        var url = endpoint.Contains('?', StringComparison.Ordinal)
            ? endpoint + "&q=" + Uri.EscapeDataString(query)
            : endpoint.TrimEnd('/') + "/?q=" + Uri.EscapeDataString(query);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
            throw new LocalizedServiceException("Chat.WebSearch.InvalidEndpoint");

        string html;
        try
        {
            html = await GetHtmlFollowingRedirectsAsync(requestUri, ct);
        }
        catch (LocalizedServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LocalizedServiceException("Chat.WebSearch.NetworkError");
        }

        return ParseDuckDuckGoHtml(html, topK);
    }

    private static async Task<string> GetHtmlFollowingRedirectsAsync(Uri uri, CancellationToken ct)
    {
        const int maxRedirects = 5;
        for (var hop = 0; hop <= maxRedirects; hop++)
        {
            EnsureHostAllowed(uri);
            HttpResponseMessage response;
            try
            {
                response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new LocalizedServiceException("Chat.WebSearch.NetworkError");
            }

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                    throw new LocalizedServiceException("Chat.WebSearch.NetworkError");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new LocalizedServiceException("Chat.WebSearch.Failed", (int)response.StatusCode);

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk, ct);
                    if (read == 0)
                        break;
                    if (buffer.Length + read > MaxDownloadBytes)
                        break;
                    buffer.Write(chunk, 0, read);
                }

                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        throw new LocalizedServiceException("Chat.WebSearch.NetworkError");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.RedirectKeepVerb
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void EnsureHostAllowed(Uri uri)
    {
        if (ChatUrlHostGuard.IsBlocked(uri))
            throw new LocalizedServiceException("Chat.Url.HostNotAllowed");
    }

    /// <summary>単体テスト用。DuckDuckGo HTML 風の結果ブロックを抽出する。</summary>
    internal static IReadOnlyList<ChatWebSearchHit> ParseDuckDuckGoHtml(string html, int topK)
    {
        if (string.IsNullOrWhiteSpace(html) || topK <= 0)
            return Array.Empty<ChatWebSearchHit>();

        var hits = new List<ChatWebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snippets = SnippetBlock.Matches(html);
        var snippetIndex = 0;

        foreach (Match match in ResultBlock.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
            var title = StripTags(WebUtility.HtmlDecode(match.Groups["title"].Value));
            href = UnwrapDuckDuckGoRedirect(href);
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || ChatUrlHostGuard.IsBlocked(uri))
                continue;
            if (!seen.Add(uri.AbsoluteUri))
                continue;

            var snippet = "";
            if (snippetIndex < snippets.Count)
            {
                snippet = StripTags(WebUtility.HtmlDecode(snippets[snippetIndex].Groups["snippet"].Value));
                snippetIndex++;
            }

            hits.Add(new ChatWebSearchHit(title, uri.AbsoluteUri, snippet));
            if (hits.Count >= topK)
                break;
        }

        return hits;
    }

    internal static string UnwrapDuckDuckGoRedirect(string href)
    {
        href = href.Trim();
        if (href.StartsWith("//", StringComparison.Ordinal))
            href = "https:" + href;

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return href;

        // duckduckgo.com/l/?uddg=<urlencoded>
        if (!uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase))
            return href;

        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("uddg", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = Uri.UnescapeDataString(kv[1]);
                if (Uri.TryCreate(decoded, UriKind.Absolute, out _))
                    return decoded;
            }
        }

        return href;
    }

    private static string StripTags(string value)
    {
        var t = Regex.Replace(value ?? "", "<[^>]+>", " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
