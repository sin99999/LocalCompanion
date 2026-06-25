namespace LocalCompanion.Models;

/// <summary>会話ログを1本のプレーンテキストに連結するときの1発言分。</summary>
public sealed class ChatMessageDisplayPart
{
    public string Header { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string ReasoningText { get; init; } = string.Empty;

    public bool ApplySentenceBreaks { get; init; } = true;
}
