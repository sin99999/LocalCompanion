using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 調査意図を検出し、Web 検索→上位ページ取得でコンテキストを足す。
/// 命名上の Agent は調査前処理のみ。マルチエージェント実行ではない。
/// </summary>
internal static class ChatAgentResearchEnricher
{
    /// <summary>ネット調査だと明示している語（法令・資料より優先）。</summary>
    private static readonly Regex HardExplicitWebCue = new(
        @"(?:ウェブ|ネット|インターネット|web)(?:で|から|やら(?:から)?|とか(?:から)?|上で|上から|を?検索)|search\s+(?:the\s+)?(?:web|online)|google|オンラインで",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>調査っぽいが、法令・登録資料より後ろ（「最新情報」＋刑法は RAG）。</summary>
    private static readonly Regex SoftExplicitWebCue = new(
        @"(最新情報|look\s*up)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>調査っぽいが、RAG／資料検索と誤爆しやすい語。</summary>
    private static readonly Regex SoftResearchCue = new(
        @"(調べて|検索して)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>代名詞だけの検索クエリ（前ターンなしでは Web にしない）。</summary>
    private static readonly Regex WeakWebQuery = new(
        @"^(?:それ|あれ|これ|その|もっと詳しく|詳しく|詳細)を?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>ローカル資料（RAG）を探す話。Web に飛ばさない。</summary>
    private static readonly Regex LocalRagSearchCue = new(
        @"(?:RAG|ＲＡＧ)|資料(?:から|を|で|の)?検索|登録資料|資料DB|資料ベース",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>法令・条文っぽいローカル資料向けクエリ（「調べて」でも Web にしない）。</summary>
    private static readonly Regex LocalLegalCorpusCue = new(
        @"(?:刑法|刑事訴訟法|労働基準法|労基法|民法|会社法|著作権法|特許法|国外犯|条文|第\s*\d+\s*条|\d+\s*条)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikeResearchIntent(string? message, string? previousUserMessage = null)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length < 4)
            return false;

        // 「ウェブで」等は明示 Web。法令チェックより先
        if (HardExplicitWebCue.IsMatch(message))
            return true;

        if (!SoftResearchCue.IsMatch(message) && !SoftExplicitWebCue.IsMatch(message))
            return false;

        // 「資料から検索して」「刑法235条を調べて」「それを調べて」（前が法令）はローカル RAG
        if (LocalRagSearchCue.IsMatch(message) || LooksLikeLocalCorpusResearch(message, previousUserMessage))
            return false;

        // 代名詞だけ＋前ターンなしは Web しても空振りなので止める
        var probeQuery = BuildSearchQuery(message, previousUserMessage);
        if (probeQuery.Length < 2 || WeakWebQuery.IsMatch(probeQuery))
            return false;

        return true;
    }

    internal static bool LooksLikeLocalCorpusResearch(string message, string? previousUserMessage = null)
    {
        if (HasHardLocalCue(message))
            return true;

        var stripped = StripSoftResearchCues(message);
        if (stripped.Length < 2)
            return false;

        if (HasHardLocalCue(stripped))
            return true;

        // 追質問は RAG と同じく前ターンを載せて判定（「それを調べて」誤爆防止）
        var plan = RagQueryPlanner.Plan(stripped, previousUserMessage);
        if (plan.Intent is not RagQueryIntent.General)
            return true;

        return RagConversationGate.Resolve(plan, stripped) != RagConversationMode.Skip;
    }

    private static string StripSoftResearchCues(string message)
    {
        var q = SoftResearchCue.Replace(message.Trim(), " ");
        return Regex.Replace(q, @"\s+", " ").Trim();
    }

    private static bool HasHardLocalCue(string message)
    {
        if (LocalLegalCorpusCue.IsMatch(message))
            return true;
        if (RagArticleQueryParser.TryGetArticleNumber(message, out _))
            return true;
        return RagLegalQueryContext.LooksLikeLegalArticleQuery(message, sourceHint: null);
    }

    public static string BuildSearchQuery(string message, string? previousUserMessage = null)
    {
        var source = message.Trim();
        if (!string.IsNullOrWhiteSpace(previousUserMessage)
            && NeedsTopicFromHistoryForWeb(source))
        {
            source = RagSearchQueryComposer.Compose(source, previousUserMessage);
        }

        var q = source;
        q = HardExplicitWebCue.Replace(q, " ");
        q = SoftExplicitWebCue.Replace(q, " ");
        q = SoftResearchCue.Replace(q, " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();
        // 保存依頼の尾は検索ノイズになるので軽く落とす
        q = Regex.Replace(
            q,
            @"[、,]?\s*(?:デスクトップ|desktop|ドキュメント|downloads?|ダウンロード).{0,40}$",
            "",
            RegexOptions.IgnoreCase);
        return q.Trim();
    }

    private static bool NeedsTopicFromHistoryForWeb(string message)
    {
        var stripped = StripSoftResearchCues(message);
        stripped = HardExplicitWebCue.Replace(stripped, " ");
        stripped = SoftExplicitWebCue.Replace(stripped, " ");
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
        if (stripped.Length < 2 || WeakWebQuery.IsMatch(stripped))
            return true;
        return stripped.Length < 28;
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
        string? previousUserMessage = null,
        CancellationToken ct = default)
    {
        if (!webSearchEnabled)
            return req;

        // メッセージに URL がある場合はインライン取得側に任せる
        if (ChatMessageUrlExtractor.Extract(req.Message, 1).Count > 0)
            return req;

        if (!LooksLikeResearchIntent(req.Message, previousUserMessage))
            return req;

        var query = BuildSearchQuery(req.Message, previousUserMessage);
        if (query.Length < 2 || WeakWebQuery.IsMatch(query))
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
