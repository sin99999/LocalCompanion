namespace LocalCompanion.Services;

public sealed record ConversationThreadPreview(
    string SessionId,
    string PresetKey,
    string Title,
    string LastSnippet,
    string LastAt);
