namespace LocalCompanion.Models;
/// <summary>取り込み前の1チャンク（DB 保存前）。</summary>
public sealed record RagChunkDraft(
    string Text,
    string EmbeddingText,
    string ChunkId,
    string HeaderText,
    int HeaderLevel,
    int Page,
    string Chapter,
    string Section,
    string Subsection,
    string ParentText = "",
    int ArticleMain = 0,
    int ArticleSub = 0,
    long ArticleSortKey = 0,
    string PenaltyLead = "",
    string ChunkKind = "section",
    string EntryKey = "",
    string DefinitionLead = "",
    string SectionPath = "",
    string DocKind = "general");

/// <summary>RAG 検索ヒット（プロンプト・UI 用）。</summary>
public sealed record RagSearchHit(
    string Text,
    string Source,
    string HeaderText,
    int Page,
    string ChunkId,
    string ParentText = "",
    string PenaltyLead = "",
    string VerbatimQuote = "",
    string DefinitionLead = "")
{
    public long ArticleSortKey =>
        RagArticleSortKeyHelper.TryParse(HeaderText, out var key) ? key : 0;

    public string SourceFileName =>
        string.IsNullOrWhiteSpace(Source) ? Source : Path.GetFileName(Source);

    public string PromptText =>
        !string.IsNullOrWhiteSpace(ParentText) ? ParentText : Text;

    public string FormatSourceLabel(int index)
    {
        var parts = new List<string> { $"[{index + 1}]" };
        if (!string.IsNullOrWhiteSpace(SourceFileName))
            parts.Add(SourceFileName);
        if (!string.IsNullOrWhiteSpace(HeaderText))
            parts.Add(HeaderText);
        if (Page > 0)
            parts.Add($"p.{Page}");
        return string.Join(" / ", parts);
    }

    public string FormatForPrompt(int index)
    {
        var label = FormatSourceLabel(index);
        var body = PromptText;
        var quote = !string.IsNullOrWhiteSpace(PenaltyLead) ? PenaltyLead
            : !string.IsNullOrWhiteSpace(DefinitionLead) ? DefinitionLead
            : VerbatimQuote;
        if (!string.IsNullOrWhiteSpace(PenaltyLead))
            return $"{label}\n【資料記載の罰則文言（引用必須）】{quote}\n{body}";
        if (!string.IsNullOrWhiteSpace(DefinitionLead))
            return $"{label}\n【資料記載の定義（引用必須）】{quote}\n{body}";
        if (!string.IsNullOrWhiteSpace(quote))
            return $"{label}\n【資料記載の引用】{quote}\n{body}";
        return $"{label}\n{body}";
    }
}

public sealed record RagDocument(string Source, string Text);
