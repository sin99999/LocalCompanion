namespace LocalCompanion.Models;

public enum ChatExportTargetKind
{
    Desktop,
    Directory,
    Documents,
    Downloads,
    UserData,
    AppRoot,
    RemovableStorage,
}

public sealed record ChatExportTarget(
    ChatExportTargetKind Kind,
    string? DirectoryPath = null);

public sealed record ChatExportRequest(
    string Query,
    string? FileNameStem,
    string Extension,
    ChatExportTarget Target);

/// <summary>デスクトップ保存用に LLM が整形したタイトルと本文。</summary>
public sealed record ChatExportDocument(string Title, string Body);
