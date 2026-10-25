namespace LocalCompanion.Models;

/// <summary>会話の計算。既定は llama.cpp。Strata は同じ 127.0.0.1:8080 を使うので同時には立てない。</summary>
public static class InferenceEngineChoices
{
    public const string LlamaCpp = "llama.cpp";

    public const string Strata = "strata";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Strata, StringComparison.OrdinalIgnoreCase)
            ? Strata
            : LlamaCpp;
}
