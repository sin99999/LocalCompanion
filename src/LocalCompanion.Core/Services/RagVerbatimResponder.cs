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

        return plan.Intent switch
        {
            RagQueryIntent.Penalty => TryFormatPenalty(hits, japanese, out reply),
            RagQueryIntent.Article => TryFormatArticle(hits, japanese, out reply),
            RagQueryIntent.Boundary => TryFormatBoundary(hits, japanese, out reply),
            RagQueryIntent.Definition => TryFormatDefinition(hits, japanese, out reply),
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

    private static bool TryFormatArticle(IReadOnlyList<RagSearchHit> hits, bool japanese, out string reply)
    {
        reply = "";
        var hit = hits[0];
        var body = hit.PromptText;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var header = string.IsNullOrWhiteSpace(hit.HeaderText) ? "" : hit.HeaderText + "\n\n";
        reply = japanese
            ? $"{header}{body}\n\n出典: {hit.FormatSourceLabel(0)}"
            : $"{header}{body}\n\nSource: {hit.FormatSourceLabel(0)}";
        return true;
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
