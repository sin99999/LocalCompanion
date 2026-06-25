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
    string Subsection);

/// <summary>RAG 検索ヒット（プロンプト・UI 用）。</summary>
public sealed record RagSearchHit(
    string Text,
    string Source,
    string HeaderText,
    int Page,
    string ChunkId)
{
    public string SourceFileName =>
        string.IsNullOrWhiteSpace(Source) ? Source : Path.GetFileName(Source);

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

    public string FormatForPrompt(int index) =>
        $"{FormatSourceLabel(index)}\n{Text}";
}

public sealed record RagDocument(string Source, string Text);
