namespace LocalCompanion;

/// <summary>初回 bootstrap で自動 DL する既定 Gemma 4 E2B（Google q4_0）。</summary>
public static class LlamaDefaultModel
{
    public const string ChatFileName = "gemma-4-E2B_q4_0-it.gguf";
    public const string ChatUrl =
        "https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf/resolve/main/gemma-4-E2B_q4_0-it.gguf?download=true";

    public const string MmprojFileName = "gemma-4-E2B-it-mmproj.gguf";
    public const string MmprojUrl =
        "https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf/resolve/main/gemma-4-E2B-it-mmproj.gguf";

    public const string HuggingFaceRepoId = "google/gemma-4-E2B-it-qat-q4_0-gguf";

    public static bool IsE2BMmprojFileName(string fileName) =>
        string.Equals(fileName, "gemma-4-E2B-it-mmproj.gguf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, MmprojFileName, StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(
            fileName, "^mmproj-(F16|BF16)\\.gguf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsMmprojFileName(string fileName) =>
        fileName.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("-mmproj.gguf", StringComparison.OrdinalIgnoreCase);
}

