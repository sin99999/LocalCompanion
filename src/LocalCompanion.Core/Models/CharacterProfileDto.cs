namespace LocalCompanion.Models;

public sealed record CharacterProfileDto(
    string Name,
    string Persona,
    string SpeakingStyle,
    double Temperature,
    double TopP,
    int TopK,
    int ContextLength,
    int MaxOutputTokens);
