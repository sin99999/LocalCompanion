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
