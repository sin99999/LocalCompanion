namespace LocalCompanion.Models;

public sealed record UserMemoryRecord(
    long Id,
    string Content,
    string MemoryPath,
    string SourceSessionId,
    string CreatedAt);

public sealed record ChatSearchHit(
    long MessageId,
    string SessionId,
    string PresetKey,
    string Role,
    string Content,
    string SessionTitle,
    string CreatedAt);
