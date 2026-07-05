using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 章・節・条・Markdown 見出しなど文書構造を優先してチャンク化する。
/// 構造が取れない場合は段落ベースの分割にフォールバックする。
/// </summary>
internal static class RagStructuralChunker
{
    private static readonly Regex PageMarker = new(
        @"^---\s*(?:ページ|Page)\s*(\d+)\s*---\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<RagChunkDraft> CreateChunks(
        string text,
        string source,
        int size,
        int overlap,
        RagDocumentKind docKind = RagDocumentKind.General)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<RagChunkDraft>();

        text = text.Replace("\r\n", "\n").Trim();
        var docKindValue = RagDocumentProfileDetector.ToStorageValue(docKind);
        var sections = SplitIntoSections(text);
        if (sections.Count == 0)
            return FallbackChunks(text, source, size, overlap, docKind, docKindValue);

        var drafts = new List<RagChunkDraft>();
        foreach (var section in sections)
        {
            var body = section.Body.Trim();
            if (body.Length < 10)
                continue;

            if (body.Length <= size)
            {
                drafts.Add(CreateDraft(section, body, source, partIndex: 0, parentText: "", docKind, docKindValue));
                continue;
            }

            var fullSectionText = BuildSectionText(section, body);
            var part = 0;
            foreach (var slice in RagTextChunker.SplitOversized(body, size, overlap))
            {
                if (slice.Trim().Length < 10)
                    continue;
                drafts.Add(CreateDraft(section, slice.Trim(), source, part, fullSectionText, docKind, docKindValue));
                part++;
            }
        }

        return drafts.Count > 0 ? drafts : FallbackChunks(text, source, size, overlap, docKind, docKindValue);
    }

    private static IReadOnlyList<RagChunkDraft> FallbackChunks(
        string text,
        string source,
        int size,
        int overlap,
        RagDocumentKind docKind,
        string docKindValue)
    {
        var list = new List<RagChunkDraft>();
        var index = 0;
        foreach (var chunk in RagTextChunker.ChunkText(text, size, overlap))
        {
            if (string.IsNullOrWhiteSpace(chunk))
                continue;
            var (entryKey, definitionLead, chunkKind) = RagGenericFieldExtractor.EnrichFallback(chunk, docKind);
            list.Add(new RagChunkDraft(
                Text: chunk,
                EmbeddingText: chunk,
                ChunkId: $"fallback_{index++}",
                HeaderText: "",
                HeaderLevel: 0,
                Page: 0,
                Chapter: "",
                Section: "",
                Subsection: "",
                ParentText: "",
                ChunkKind: chunkKind,
                EntryKey: entryKey,
                DefinitionLead: definitionLead,
                DocKind: docKindValue));
        }
        return list;
    }

    private static RagChunkDraft CreateDraft(
        SectionBlock section,
        string body,
        string source,
        int partIndex,
        string parentText,
        RagDocumentKind docKind,
        string docKindValue)
    {
        var chunkId = BuildChunkId(section, partIndex);
        var embedding = BuildEmbeddingText(section, body);

        var articleMain = 0;
        var articleSub = 0;
        long articleSortKey = 0;
        var penaltyLead = "";
        var chunkKind = "section";

        var useLegal = docKind == RagDocumentKind.Legal
            || RagArticleQueryParser.TryParseArticleSortKey(section.HeaderText, out _);
        if (useLegal)
        {
            (articleMain, articleSub, articleSortKey) = LegalFieldExtractor.ParseArticle(section.HeaderText, body);
            penaltyLead = LegalFieldExtractor.ExtractPenaltyLead(section.HeaderText, body, parentText);
            chunkKind = LegalFieldExtractor.ResolveChunkKind(section.HeaderText, parentText);
        }
        else
        {
            chunkKind = LegalFieldExtractor.ResolveChunkKind(section.HeaderText, parentText);
        }

        var generic = RagGenericFieldExtractor.Enrich(
            section.HeaderText,
            body,
            parentText,
            section.HeaderLevel,
            section.Chapter,
            section.Section,
            section.Subsection,
            docKind);

        var entryKey = generic.EntryKey;
        var definitionLead = generic.DefinitionLead;
        var sectionPath = generic.SectionPath;
        if (articleSortKey == 0 && generic.ChunkKind is "definition" or "glossary" or "faq")
            chunkKind = generic.ChunkKind;

        return new RagChunkDraft(
            Text: body,
            EmbeddingText: embedding,
            ChunkId: chunkId,
            HeaderText: section.HeaderText,
            HeaderLevel: section.HeaderLevel,
            Page: section.Page,
            Chapter: section.Chapter,
            Section: section.Section,
            Subsection: section.Subsection,
            ParentText: parentText,
            ArticleMain: articleMain,
            ArticleSub: articleSub,
            ArticleSortKey: articleSortKey,
            PenaltyLead: penaltyLead,
            ChunkKind: chunkKind,
            EntryKey: entryKey,
            DefinitionLead: definitionLead,
            SectionPath: sectionPath,
            DocKind: docKindValue);
    }

    private static string BuildSectionText(SectionBlock section, string body)
    {
        if (string.IsNullOrWhiteSpace(section.HeaderText))
            return body;

        return section.HeaderText + "\n" + body;
    }

    private static string BuildEmbeddingText(SectionBlock section, string body)
    {
        var prefixParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(section.HeaderText))
            prefixParts.Add(section.HeaderText);
        if (section.Page > 0)
            prefixParts.Add($"ページ {section.Page}");
        if (prefixParts.Count == 0)
            return body;
        return string.Join(" | ", prefixParts) + "\n" + body;
    }

    private static string BuildChunkId(SectionBlock section, int partIndex)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(section.Chapter))
            parts.Add($"ch{section.Chapter}");
        if (!string.IsNullOrWhiteSpace(section.Section))
            parts.Add($"sec{SanitizeId(section.Section)}");
        if (!string.IsNullOrWhiteSpace(section.Subsection))
            parts.Add($"sub{SanitizeId(section.Subsection)}");
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(section.HeaderText))
            parts.Add($"h{section.HeaderLevel}_{SanitizeId(section.HeaderText)}");
        if (section.Page > 0)
            parts.Add($"p{section.Page}");
        if (partIndex > 0)
            parts.Add($"part{partIndex + 1}");
        return parts.Count > 0 ? string.Join("_", parts) : $"block_{partIndex}";
    }

    private static string SanitizeId(string value) =>
        Regex.Replace(value, @"[^\w\.]+", "_").Trim('_');

    private static List<SectionBlock> SplitIntoSections(string text)
    {
        var lines = text.Split('\n');
        var sections = new List<SectionBlock>();
        var currentPage = 0;
        var lastChapter = "";
        var lastSection = "";
        var lastSubsection = "";
        SectionBlock? current = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (current is null)
                return;
            current = current with { Body = body.ToString() };
            if (current.Body.Trim().Length >= 10)
                sections.Add(current);
            body.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var pageMatch = PageMarker.Match(line.Trim());
            if (pageMatch.Success)
            {
                currentPage = int.Parse(pageMatch.Groups[1].Value);
                continue;
            }

            if (TryParseHeader(line, out var header))
            {
                Flush();
                if (line.TrimStart().StartsWith('#'))
                {
                    if (header.Level <= 2)
                    {
                        lastChapter = header.Text;
                        lastSection = "";
                        lastSubsection = "";
                    }
                    else if (header.Level == 3)
                    {
                        lastSection = header.Text;
                        lastSubsection = "";
                    }
                    else if (header.Level >= 4)
                    {
                        lastSubsection = header.Text;
                    }

                    current = new SectionBlock(
                        HeaderText: header.Text,
                        HeaderLevel: header.Level,
                        Page: currentPage,
                        Chapter: lastChapter,
                        Section: lastSection,
                        Subsection: lastSubsection,
                        Body: "");
                }
                else
                {
                    current = new SectionBlock(
                        HeaderText: header.Text,
                        HeaderLevel: header.Level,
                        Page: currentPage,
                        Chapter: header.Chapter,
                        Section: header.Section,
                        Subsection: header.Subsection,
                        Body: "");
                }

                continue;
            }

            if (current is null)
            {
                current = new SectionBlock("", 0, currentPage, "", "", "", "");
            }

            body.AppendLine(rawLine);
        }

        Flush();
        return sections;
    }

    private static bool TryParseHeader(string line, out HeaderInfo header)
    {
        header = default!;
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed.StartsWith('#'))
        {
            var level = 0;
            while (level < trimmed.Length && trimmed[level] == '#')
                level++;
            if (level is < 1 or > 6)
                return false;
            var text = trimmed[level..].Trim();
            if (text.Length == 0)
                return false;
            header = new HeaderInfo(level, text, "", "", "");
            return true;
        }

        var partMatch = Regex.Match(trimmed, @"^第\s*([IVX]+)\s*部");
        if (partMatch.Success)
        {
            header = new HeaderInfo(1, trimmed, partMatch.Groups[1].Value, "", "");
            return true;
        }

        var chapterMatch = Regex.Match(trimmed, @"^第\s*(\d+)\s*章");
        if (chapterMatch.Success)
        {
            header = new HeaderInfo(2, trimmed, chapterMatch.Groups[1].Value, "", "");
            return true;
        }

        var articleMatch = Regex.Match(trimmed, @"^第\s*(\d+)\s*条");
        if (articleMatch.Success)
        {
            header = new HeaderInfo(3, trimmed, "", articleMatch.Groups[1].Value, "");
            return true;
        }

        var sectionTitleMatch = Regex.Match(trimmed, @"^第\s*(\d+)\s*節");
        if (sectionTitleMatch.Success)
        {
            header = new HeaderInfo(3, trimmed, "", sectionTitleMatch.Groups[1].Value, "");
            return true;
        }

        var subsectionMatch = Regex.Match(trimmed, @"^(\d+)\.(\d+)\.(\d+)\b");
        if (subsectionMatch.Success)
        {
            var chapter = subsectionMatch.Groups[1].Value;
            var section = $"{subsectionMatch.Groups[1].Value}.{subsectionMatch.Groups[2].Value}";
            var subsection = subsectionMatch.Value.TrimEnd('.');
            header = new HeaderInfo(4, trimmed, chapter, section, subsection);
            return true;
        }

        var numberedSectionMatch = Regex.Match(trimmed, @"^(\d+)\.(\d+)\b");
        if (numberedSectionMatch.Success)
        {
            var chapter = numberedSectionMatch.Groups[1].Value;
            var section = numberedSectionMatch.Value.TrimEnd('.');
            header = new HeaderInfo(3, trimmed, chapter, section, "");
            return true;
        }

        return false;
    }

    private sealed record HeaderInfo(
        int Level,
        string Text,
        string Chapter,
        string Section,
        string Subsection);

    private sealed record SectionBlock(
        string HeaderText,
        int HeaderLevel,
        int Page,
        string Chapter,
        string Section,
        string Subsection,
        string Body);
}
