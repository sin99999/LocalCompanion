namespace LocalCompanion.Models;

/// <summary>会話ログ1本表示用テキストと推論ハイライト位置。</summary>
public sealed class ChatConversationDisplayText
{
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<ChatTextRange> ReasoningRanges { get; init; } = Array.Empty<ChatTextRange>();
}

public readonly record struct ChatTextRange(int Start, int Length);
