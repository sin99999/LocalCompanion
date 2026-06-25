namespace LocalCompanion.Models;

public enum ChatDisplayBlockKind
{
    Paragraph,
    List,
    Table,
    Code,
}

public sealed class ChatDisplayBlock
{
    public ChatDisplayBlockKind Kind { get; init; }

    public IReadOnlyList<string> ParagraphLines { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ListItems { get; init; } = Array.Empty<string>();

    public bool ListOrdered { get; init; }

    public IReadOnlyList<string> TableHeader { get; init; } = Array.Empty<string>();

    public IReadOnlyList<IReadOnlyList<string>> TableRows { get; init; } =
        Array.Empty<IReadOnlyList<string>>();

    /// <summary>フェンス付きコードブロック（表示専用・原文は変更しない）。</summary>
    public string CodeText { get; init; } = string.Empty;
}
