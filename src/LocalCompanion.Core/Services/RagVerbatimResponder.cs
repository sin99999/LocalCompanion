using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>高信頼ヒットを LLM なしでそのまま返す。</summary>
internal static class RagVerbatimResponder
{
    public static bool TryFormat(
        RagQueryPlan plan,
        IReadOnlyList<RagSearchHit> hits,
        bool japanese,
        out string reply)
    {
        reply = "";
        if (plan.ResponseMode != RagResponseMode.Verbatim || hits.Count == 0)
            return false;
        if (RagArticleHitFilter.IsAmbiguousSources(plan, hits))
            return false;

        return plan.Intent switch
        {
            RagQueryIntent.Penalty => TryFormatPenalty(hits, japanese, out reply),
            RagQueryIntent.Article => TryFormatArticle(plan, hits, japanese, out reply),
            RagQueryIntent.Boundary => TryFormatBoundary(hits, japanese, out reply),
            RagQueryIntent.Definition => TryFormatDefinition(hits, japanese, out reply),
            RagQueryIntent.Faq => TryFormatFaq(hits, japanese, out reply),
            RagQueryIntent.SourceCatalog => TryFormatSourceCatalog(hits, japanese, out reply),
            _ => false,
        };
    }

    private static bool TryFormatPenalty(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        var hit = hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.PenaltyLead))
            ?? hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.VerbatimQuote));
        if (hit is null)
            return false;

        var quote = !string.IsNullOrWhiteSpace(hit.PenaltyLead) ? hit.PenaltyLead : hit.VerbatimQuote!;
        reply = japanese
            ? $"【資料記載の罰則文言】\n{quote}\n\n出典: {hit.FormatSourceLabel(0)}"
            : $"[Penalty text from materials]\n{quote}\n\nSource: {hit.FormatSourceLabel(0)}";
        return true;
    }

    private static bool TryFormatArticle(
        RagQueryPlan plan,
        IReadOnlyList<RagSearchHit> hits,
        bool japanese,
        out string reply)
    {
        reply = "";
        var keys = RagArticleHitFilter.RequestedKeys(plan);
        var ordered = new List<RagSearchHit>();
        if (plan.ArticleBindings is { Count: > 0 })
        {
            foreach (var binding in plan.ArticleBindings)
            {
                var hit = hits.FirstOrDefault(h =>
                    h.ArticleSortKey == binding.SortKey
                    && RagSourceHintResolver.MatchesHint(h.Source, binding.Hint));
                if (hit is not null)
                    ordered.Add(hit);
            }
        }
        else if (keys.Count == 0)
        {
            ordered.AddRange(hits);
        }
        else
        {
            foreach (var key in keys)
            {
                var forKey = hits.Where(h => h.ArticleSortKey == key).ToList();
                if (RagArticleHitFilter.WantsAllNamedSources(plan))
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var hit in forKey)
                    {
                        if (seen.Add(hit.SourceFileName))
                            ordered.Add(hit);
                    }
                }
                else if (forKey.Count > 0)
                {
                    ordered.Add(forKey[0]);
                }
            }
        }

        var parts = new List<string>();
        var index = 0;
        foreach (var hit in ordered)
        {
            if (!RagArticleHitFilter.MatchesRequested(plan, hit))
                continue;

            var body = StripLeadingArticleHeaderLine(hit.PromptText, hit.HeaderText);
            if (string.IsNullOrWhiteSpace(body))
                continue;

            var header = string.IsNullOrWhiteSpace(hit.HeaderText) ? "" : hit.HeaderText + "\n\n";
            parts.Add(japanese
                ? $"{header}{body}\n\n出典: {hit.FormatSourceLabel(index)}"
                : $"{header}{body}\n\nSource: {hit.FormatSourceLabel(index)}");
            index++;
        }

        if (parts.Count == 0)
            return false;

        reply = string.Join("\n\n", parts);
        return true;
    }

    private static string StripLeadingArticleHeaderLine(string body, string headerText)
    {
        if (string.IsNullOrWhiteSpace(body))
            return body;

        var lines = body.Split('\n');
        if (lines.Length == 0)
            return body;

        var first = RagArticleQueryParser.StripMarkdownHeadingPrefix(lines[0].Trim());
        if (!string.IsNullOrWhiteSpace(headerText)
            && string.Equals(first, headerText, StringComparison.Ordinal))
        {
            return string.Join('\n', lines.Skip(1)).TrimStart();
        }

        if (RagArticleQueryParser.TryParseArticleSortKey(first, out _))
            return string.Join('\n', lines.Skip(1)).TrimStart();

        return body;
    }

    private static bool TryFormatDefinition(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        var hit = hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.DefinitionLead))
            ?? hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.PromptText));
        if (hit is null)
            return false;

        var quote = !string.IsNullOrWhiteSpace(hit.DefinitionLead) ? hit.DefinitionLead : hit.PromptText;
        var term = string.IsNullOrWhiteSpace(hit.HeaderText) ? "" : hit.HeaderText + "\n\n";
        reply = japanese
            ? $"【資料記載の定義】\n{term}{quote}\n\n出典: {hit.FormatSourceLabel(0)}"
            : $"[Definition from materials]\n{term}{quote}\n\nSource: {hit.FormatSourceLabel(0)}";
        return !string.IsNullOrWhiteSpace(quote);
    }

    private static bool TryFormatFaq(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        var hit = hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.DefinitionLead))
            ?? hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.PromptText));
        if (hit is null)
            return false;

        var quote = !string.IsNullOrWhiteSpace(hit.DefinitionLead) ? hit.DefinitionLead : hit.PromptText;
        var question = string.IsNullOrWhiteSpace(hit.HeaderText) ? "" : hit.HeaderText + "\n\n";
        reply = japanese
            ? $"【資料記載の回答】\n{question}{quote}\n\n出典: {hit.FormatSourceLabel(0)}"
            : $"[Answer from materials]\n{question}{quote}\n\nSource: {hit.FormatSourceLabel(0)}";
        return !string.IsNullOrWhiteSpace(quote);
    }

    private static bool TryFormatSourceCatalog(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        if (hits.Count == 0)
            return false;

        var lines = hits
            .Select(h => h.PromptText.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0)
            return false;

        var header = japanese ? "【RAG 登録資料】" : "[RAG sources]";
        reply = header + "\n" + string.Join("\n", lines);
        return true;
    }

    private static bool TryFormatBoundary(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        var meta = hits.FirstOrDefault(h => h.ChunkId == "__boundary_meta__");
        if (meta is not null)
        {
            reply = meta.Text;
            return true;
        }

        var hit = hits[0];
        reply = japanese
            ? $"{hit.PromptText}\n\n出典: {hit.FormatSourceLabel(0)}"
            : $"{hit.PromptText}\n\nSource: {hit.FormatSourceLabel(0)}";
        return !string.IsNullOrWhiteSpace(hit.PromptText);
    }
}
