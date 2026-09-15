using System.Text;
using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 「〇〇の見た目で」のとき、登録 JSON を先に読み、無ければ Web 検索結果を添付する。
/// </summary>
internal static class ChatAppearanceLookupEnricher
{
    public static async Task<ChatRequestDto> EnrichAsync(
        ChatRequestDto req,
        IReadOnlyList<CharacterAppearancePreset> presets,
        bool webSearchEnabled,
        string? webSearchBaseUrl,
        int searchTopK,
        int fetchTopK,
        int maxCharsPerUrl,
        int maxAttachChars,
        CancellationToken ct = default)
    {
        var names = ChatAppearanceLookup.ExtractSubjectNames(req.Message);
        if (names.Count == 0)
            return req;

        var usedLocal = false;
        var usedWeb = false;
        var blocks = new List<string>();
        var urls = new List<string>();
        var loc = LocalizationService.Instance;

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var preset = ChatAppearanceLookup.Match(name, presets);
            if (preset is not null)
            {
                var appearance = ChatAppearanceLookup.ExtractAppearanceText(preset.Persona);
                if (string.IsNullOrWhiteSpace(appearance))
                    appearance = loc.Format("Chat.Appearance.LocalEmpty", preset.Name);
                blocks.Add(loc.Format("Chat.Appearance.LocalHeader", preset.Name) + "\n" + appearance);
                usedLocal = true;
                continue;
            }

            var display = ChatAppearanceLookup.NormalizeName(
                name.Contains('の', StringComparison.Ordinal)
                    ? name.Split('の', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1]
                    : name);
            if (display.Length == 0)
                display = name.Trim();

            if (!webSearchEnabled)
            {
                blocks.Add(loc.Format("Chat.Appearance.NotFound", display));
                continue;
            }

            var query = ChatAppearanceLookup.BuildWebQuery(name);
            IReadOnlyList<ChatWebSearchHit> hits;
            try
            {
                hits = await ChatWebSearchClient.SearchAsync(query, webSearchBaseUrl, searchTopK, ct);
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, "Appearance web search failed");
                blocks.Add(loc.Get("Chat.WebSearch.FailedSoft"));
                continue;
            }

            if (hits.Count == 0)
            {
                blocks.Add(loc.Format("Chat.Appearance.NotFound", display) + "\n"
                    + loc.Format("Chat.WebSearch.NoHits", query));
                continue;
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
                    StartupLog.Write(ex, $"Appearance fetch failed: {hit.Url}");
                    fetched.Add((hit.Url, hit.Title, $"[{loc.Get("Chat.Url.NetworkError")}]"));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(loc.Format("Chat.Appearance.WebHeader", display));
            sb.AppendLine(ChatAgentResearchEnricher.FormatSearchAttachment(query, hits, fetched));
            blocks.Add(sb.ToString().Trim());
            usedWeb = true;
            foreach (var hit in hits)
            {
                if (!string.IsNullOrWhiteSpace(hit.Url))
                    urls.Add(hit.Url);
            }
        }

        if (blocks.Count == 0)
            return req;

        var header = loc.Get("Chat.Appearance.Instruction");
        var body = header + "\n\n" + string.Join("\n\n----\n\n", blocks);
        if (body.Length > maxAttachChars)
            body = body[..maxAttachChars] + "\n…";

        var label = loc.Get("Chat.Appearance.AttachmentName");
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
            WebSourceUrls = usedWeb
                ? ChatWebSourceCitation.Merge(req.WebSourceUrls, urls.ToArray())
                : req.WebSourceUrls,
            UsedAppearanceLocal = usedLocal,
            UsedAppearanceWeb = usedWeb,
        };
    }
}
