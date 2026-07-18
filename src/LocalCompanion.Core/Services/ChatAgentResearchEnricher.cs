using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>調査意図を検出し、Web 検索→上位ページ取得でコンテキストを足す。</summary>
internal static class ChatAgentResearchEnricher
{
    private static readonly Regex ResearchCue = new(
        @"(調べて|検索して|ウェブで|ネットで|最新情報|look\s*up|search\s+(?:the\s+)?(?:web|online)|google|ウェブ検索)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikeResearchIntent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length < 4)
            return false;
        return ResearchCue.IsMatch(message);
    }

    public static string BuildSearchQuery(string message)
    {
        var q = message.Trim();
        q = ResearchCue.Replace(q, " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();
        // 保存依頼の尾は検索ノイズになるので軽く落とす
        q = Regex.Replace(
            q,
            @"[、,]?\s*(?:デスクトップ|desktop|ドキュメント|downloads?|ダウンロード).{0,40}$",
            "",
            RegexOptions.IgnoreCase);
        return q.Trim();
    }

    /// <summary>検索ヒットの参考URL一覧＋要約＋（任意）取得本文を整形する。</summary>
    internal static string FormatSearchAttachment(
        string query,
        IReadOnlyList<ChatWebSearchHit> hits,
        IReadOnlyList<(string Url, string Title, string Body)> fetchedPages)
    {
        var lang = LocalizationService.Instance?.Current ?? AppLanguage.Japanese;
        var table = LocalizationResources.For(lang);
        string Loc(string key) => table.TryGetValue(key, out var v) ? v : key;
        string LocFormat(string key, params object[] args) => string.Format(Loc(key), args);

        var sb = new StringBuilder();
        sb.AppendLine(LocFormat("Chat.WebSearch.ResultsHeader", query));
        sb.AppendLine();
        sb.AppendLine(Loc("Chat.WebSearch.SourcesHeader"));
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            sb.AppendLine($"{i + 1}. {hit.Title}");
            sb.AppendLine($"   {hit.Url}");
        }

        sb.AppendLine();
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            sb.AppendLine($"{i + 1}. {hit.Title}");
            sb.AppendLine(hit.Url);
            if (!string.IsNullOrWhiteSpace(hit.Snippet))
                sb.AppendLine(hit.Snippet);
            sb.AppendLine();
        }

        foreach (var page in fetchedPages)
        {
            sb.AppendLine("----");
            sb.AppendLine($"{Loc("Chat.WebSearch.PageTitle")}: {page.Title}");
            sb.AppendLine($"{Loc("Chat.WebSearch.PageUrl")}: {page.Url}");
            sb.AppendLine(page.Body);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static async Task<ChatRequestDto> EnrichAsync(
        ChatRequestDto req,
        bool webSearchEnabled,
        string? webSearchBaseUrl,
        int searchTopK,
        int fetchTopK,
        int maxCharsPerUrl,
        int maxAttachChars,
        CancellationToken ct = default)
    {
        if (!webSearchEnabled)
            return req;

        // メッセージに URL がある場合はインライン取得側に任せる
        if (ChatMessageUrlExtractor.Extract(req.Message, 1).Count > 0)
            return req;

        if (!LooksLikeResearchIntent(req.Message))
            return req;

        var query = BuildSearchQuery(req.Message);
        if (query.Length < 2)
            return req;

        IReadOnlyList<ChatWebSearchHit> hits;
        try
        {
            hits = await ChatWebSearchClient.SearchAsync(query, webSearchBaseUrl, searchTopK, ct);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Web search failed");
            var note = LocalizationService.Instance.Get("Chat.WebSearch.FailedSoft");
            return AppendNote(req, note, maxAttachChars);
        }

        if (hits.Count == 0)
        {
            var empty = LocalizationService.Instance.Format("Chat.WebSearch.NoHits", query);
            return AppendNote(req, empty, maxAttachChars);
        }

        var fetched = new List<(string Url, string Title, string Body)>();
        var fetchCount = Math.Min(fetchTopK, hits.Count);
        for (var i = 0; i < fetchCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var hit = hits[i];
            try
            {
                var (_, text) = await ChatUrlContentFetcher.FetchAsync(hit.Url, ct);
                if (text.Length > maxCharsPerUrl)
                    text = text[..maxCharsPerUrl] + "\n…";
                fetched.Add((hit.Url, hit.Title, text));
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, $"Web search fetch failed: {hit.Url}");
                fetched.Add((
                    hit.Url,
                    hit.Title,
                    $"[{LocalizationService.Instance.Get("Chat.Url.NetworkError")}]"));
            }
        }

        var body = FormatSearchAttachment(query, hits, fetched);
        if (body.Length > maxAttachChars)
            body = body[..maxAttachChars] + "\n…";

        var label = LocalizationService.Instance.Get("Chat.WebSearch.AttachmentName");
        var merged = string.IsNullOrWhiteSpace(req.AttachedText)
            ? body
            : req.AttachedText.Trim() + "\n\n----\n\n" + body;

        if (merged.Length > maxAttachChars)
            merged = merged[..maxAttachChars] + "\n…";

        return req with
        {
            AttachedText = merged,
            AttachedFileName = string.IsNullOrWhiteSpace(req.AttachedFileName)
                ? label
                : req.AttachedFileName + " + " + label,
            WebSourceUrls = ChatWebSourceCitation.Merge(
                req.WebSourceUrls,
                hits.Select(h => h.Url).ToArray()),
        };
    }

    private static ChatRequestDto AppendNote(ChatRequestDto req, string note, int maxAttachChars)
    {
        var merged = string.IsNullOrWhiteSpace(req.AttachedText)
            ? note
            : req.AttachedText.Trim() + "\n\n" + note;
        if (merged.Length > maxAttachChars)
            merged = merged[..maxAttachChars] + "\n…";

        return req with
        {
            AttachedText = merged,
            AttachedFileName = string.IsNullOrWhiteSpace(req.AttachedFileName)
                ? LocalizationService.Instance.Get("Chat.WebSearch.AttachmentName")
                : req.AttachedFileName,
        };
    }
}
