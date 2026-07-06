namespace LocalCompanion.Models;

public enum ChatExportDestination
{
    Desktop,
}

public sealed record ChatExportRequest(
    string Query,
    string? FileNameStem,
    string Extension,
    ChatExportDestination Destination);

/// <summary>デスクトップ保存用に LLM が整形したタイトルと本文。</summary>
public sealed record ChatExportDocument(string Title, string Body);
