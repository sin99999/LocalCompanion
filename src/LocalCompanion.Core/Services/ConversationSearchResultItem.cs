namespace LocalCompanion.Services;

public sealed record ConversationSearchResultItem(
    string SessionId,
    string Title,
    string Snippet,
    string Role);
