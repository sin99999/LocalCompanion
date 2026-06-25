namespace LocalCompanion.Models;

public sealed record GgufFileInfo(string FileName, string FullPath, double SizeGb, bool Exists);

public sealed record LocalModelsResponse(
    string ModelsDirectory,
    IReadOnlyList<GgufFileInfo> ChatModels,
    IReadOnlyList<GgufFileInfo> MmprojFiles,
    ModelSelectionDto Selection,
    string? SuggestedMmproj,
    string? AdditionalModelsFolder = null);

public sealed record ModelSelectionDto(
    string? ModelFileName,
    string? MmprojFileName,
    string? ModelFullPath = null,
    string? MmprojFullPath = null);

public sealed record SelectModelRequest(
    string ModelFileName,
    string? MmprojFileName,
    string? ModelFullPath = null);
