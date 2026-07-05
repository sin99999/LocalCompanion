using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>用語・定義・FAQ 向けの汎用 ingest メタデータ。</summary>
internal static class RagGenericFieldExtractor
{
    private static readonly Regex TermDashDefinition = new(
        @"^\*\*(.+?)\*\*\s*[—\-–:：]\s*(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex PlainTermDefinition = new(
        @"^(.{1,40}?)[：:]\s*(.{10,})$",
        RegexOptions.Compiled);

    private static readonly Regex FaqQuestionHeader = new(
        @"^(?:Q[:：\.]|質問[:：]|【?Q\d*】?)\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (string EntryKey, string DefinitionLead, string ChunkKind, string SectionPath) Enrich(
        string headerText,
        string body,
        string parentText,
        int headerLevel,
        string chapter,
        string section,
        string subsection,
        RagDocumentKind docKind)
    {
        var sectionPath = BuildSectionPath(headerText, chapter, section, subsection);
        var entryKey = "";
        var definitionLead = "";
        var chunkKind = ResolveBaseKind(headerText, parentText);

        if (TryParseFaqPair(headerText, body, out var faqQ, out var faqA))
        {
            entryKey = RagEntryKeyNormalizer.Normalize(faqQ);
            definitionLead = faqA;
            chunkKind = "faq";
        }
        else if (TryExtractInlineDefinition(headerText, body, out var inlineTerm, out var inlineDef))
        {
            entryKey = RagEntryKeyNormalizer.Normalize(inlineTerm);
            definitionLead = inlineDef;
            chunkKind = "definition";
        }
        else if (!string.IsNullOrWhiteSpace(headerText))
        {
            entryKey = RagEntryKeyNormalizer.Normalize(headerText);
            definitionLead = ExtractDefinitionLead(headerText, body);
            if (!string.IsNullOrWhiteSpace(definitionLead))
                chunkKind = docKind == RagDocumentKind.Glossary ? "glossary" : "definition";
        }

        if (IsFaqBlock(headerText, body) && chunkKind != "faq")
            chunkKind = "faq";

        return (entryKey, definitionLead, chunkKind, sectionPath);
    }

    internal static bool TryParseFaqPair(string headerText, string body, out string question, out string answer)
    {
        question = "";
        answer = "";

        var qMatch = FaqQuestionHeader.Match(headerText);
        if (qMatch.Success)
            question = qMatch.Groups[1].Value.Trim();
        else if (!string.IsNullOrWhiteSpace(headerText) && (headerText.EndsWith('？') || headerText.EndsWith('?')))
            question = headerText.Trim().TrimEnd('？', '?');

        if (string.IsNullOrWhiteSpace(question))
            return false;

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("A:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("A：", StringComparison.Ordinal)
                || line.StartsWith("回答：", StringComparison.Ordinal)
                || line.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':') >= 0 ? line.IndexOf(':') : line.IndexOf('：');
                answer = idx >= 0 ? line[(idx + 1)..].Trim() : line;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(answer))
            answer = ExtractDefinitionLead(headerText, body);

        return !string.IsNullOrWhiteSpace(answer) && answer.Length >= 4;
    }

    public static (string EntryKey, string DefinitionLead, string ChunkKind) EnrichFallback(
        string body,
        RagDocumentKind docKind)
    {
        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        if (TryExtractInlineDefinition(firstLine, body, out var term, out var def))
        {
            return (
                RagEntryKeyNormalizer.Normalize(term),
                def,
                docKind == RagDocumentKind.Glossary ? "glossary" : "definition");
        }

        return ("", "", "fallback");
    }

    private static string ResolveBaseKind(string headerText, string parentText)
    {
        if (string.IsNullOrWhiteSpace(headerText) && string.IsNullOrWhiteSpace(parentText))
            return "fallback";
        return !string.IsNullOrWhiteSpace(parentText) ? "split" : "section";
    }

    private static string BuildSectionPath(string headerText, string chapter, string section, string subsection)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(chapter))
            parts.Add($"第{chapter}章");
        if (!string.IsNullOrWhiteSpace(section))
            parts.Add(section.Contains('条', StringComparison.Ordinal) ? $"第{section}条" : section);
        if (!string.IsNullOrWhiteSpace(subsection))
            parts.Add(subsection);
        if (!string.IsNullOrWhiteSpace(headerText) && !parts.Contains(headerText))
            parts.Add(headerText);
        return string.Join(" > ", parts);
    }

    private static bool TryExtractInlineDefinition(
        string headerOrFirstLine,
        string body,
        out string term,
        out string definition)
    {
        term = "";
        definition = "";

        var line = headerOrFirstLine.Trim();
        var dash = TermDashDefinition.Match(line);
        if (dash.Success)
        {
            term = dash.Groups[1].Value.Trim();
            definition = dash.Groups[2].Value.Trim();
            return term.Length > 0 && definition.Length >= 4;
        }

        var plain = PlainTermDefinition.Match(line);
        if (plain.Success && !line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            term = plain.Groups[1].Value.Trim();
            definition = plain.Groups[2].Value.Trim();
            if (term.Length is > 0 and <= 40 && definition.Length >= 8)
                return true;
        }

        if (string.IsNullOrWhiteSpace(line) && !string.IsNullOrWhiteSpace(body))
        {
            var first = body.Split('\n', 2, StringSplitOptions.TrimEntries)[0];
            return TryExtractInlineDefinition(first, body, out term, out definition);
        }

        return false;
    }

    private static string ExtractDefinitionLead(string headerText, string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
            return "";

        if (TryExtractInlineDefinition(headerText, body, out _, out var inline))
            return inline;

        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.Equals(headerText, StringComparison.Ordinal))
                continue;
            if (line.StartsWith('#'))
                continue;
            if (line.Length >= 4)
                return line.Length <= 400 ? line : line[..400];
        }

        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private static bool IsFaqBlock(string headerText, string body)
    {
        if (FaqQuestionHeader.IsMatch(headerText))
            return true;

        var sample = (headerText + "\n" + body)[..Math.Min(500, headerText.Length + body.Length)];
        return sample.Contains("A:", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("A：", StringComparison.Ordinal)
            || sample.Contains("回答：", StringComparison.Ordinal);
    }
}
