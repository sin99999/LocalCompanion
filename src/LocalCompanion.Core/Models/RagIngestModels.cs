namespace LocalCompanion.Models;

public sealed record RagIngestStats(
    string DocKind,
    int TotalChunks,
    int DefinitionChunks,
    int FaqChunks,
    int ArticleChunks,
    int EmbedSkipped)
{
    public static RagIngestStats Empty => new("General", 0, 0, 0, 0, 0);
}
