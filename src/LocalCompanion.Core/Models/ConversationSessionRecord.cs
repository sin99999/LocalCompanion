namespace LocalCompanion.Models;

public sealed record ConversationSessionRecord(
    string Id,
    string PresetKey,
    string Title,
    string Summary,
    string CreatedAt,
    string UpdatedAt,
    string? ClosedAt);
