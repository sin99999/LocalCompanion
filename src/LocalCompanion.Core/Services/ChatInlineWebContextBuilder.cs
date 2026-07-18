using System.Text;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>メッセージ内 URL を取得し、添付テキストとして結合する。</summary>
internal static class ChatInlineWebContextBuilder
{
    public const int DefaultMaxUrls = 3;
    public const int DefaultMaxCharsPerUrl = 12_000;

    public static async Task<ChatRequestDto> EnrichAsync(
        ChatRequestDto req,
        int maxUrls,
        int maxCharsPerUrl,
        int maxAttachChars,
        CancellationToken ct = default)
    {
        var urls = ChatMessageUrlExtractor.Extract(req.Message, maxUrls);
        if (urls.Count == 0)
            return req;

        var blocks = new List<string>();
        var names = new List<string>();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (displayName, text) = await ChatUrlContentFetcher.FetchAsync(url, ct);
                if (text.Length > maxCharsPerUrl)
                    text = text[..maxCharsPerUrl] + "\n…";
                blocks.Add(text);
                names.Add(displayName);
            }
            catch (Exception ex) when (ex is LocalizedServiceException or HttpRequestException or TaskCanceledException)
            {
                var reason = ex is LocalizedServiceException lex
                    ? LocalizationService.Instance.Format(lex.LocalizationKey, lex.FormatArgs)
                    : LocalizationService.Instance.Get("Chat.Url.NetworkError");
                blocks.Add($"URL: {url}\n\n[{reason}]");
                names.Add(url);
                StartupLog.Write(ex, $"Inline URL fetch failed: {url}");
            }
        }

        if (blocks.Count == 0)
            return req;

        var fetched = string.Join("\n\n----\n\n", blocks);
        var merged = string.IsNullOrWhiteSpace(req.AttachedText)
            ? fetched
            : req.AttachedText.Trim() + "\n\n----\n\n" + fetched;

        if (merged.Length > maxAttachChars)
            merged = merged[..maxAttachChars] + "\n…";

        var fileName = names.Count == 1
            ? names[0]
            : LocalizationService.Instance.Format("Chat.Url.InlineBundleName", names.Count);

        return req with
        {
            AttachedText = merged,
            AttachedFileName = string.IsNullOrWhiteSpace(req.AttachedFileName)
                ? fileName
                : req.AttachedFileName + " + " + fileName,
            WebSourceUrls = ChatWebSourceCitation.Merge(req.WebSourceUrls, urls),
        };
    }
}
