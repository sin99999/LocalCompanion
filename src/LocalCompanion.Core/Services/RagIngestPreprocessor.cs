namespace LocalCompanion.Services;

/// <summary>取込前の形式別前処理（HTML 構造化など）。</summary>
internal static class RagIngestPreprocessor
{
    public static string Preprocess(string source, string text, RagIngestOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (options.UseHtmlMarkdown && ext is ".html" or ".htm")
            text = RagHtmlStructuredExtractor.ToStructuredMarkdown(text);

        return text;
    }
}

public sealed record RagIngestOptions(
    bool UseHtmlMarkdown = true,
    bool UseLlmStructurer = false,
    bool SaveStructurerCache = false,
    bool UsePdfLayoutReader = false);
