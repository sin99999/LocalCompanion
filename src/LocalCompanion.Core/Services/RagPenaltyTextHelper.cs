namespace LocalCompanion.Services;

internal static class RagPenaltyTextHelper
{
    public static string? ExtractLeadingPenaltySentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length < 10)
                continue;

            if (!line.Contains('処', StringComparison.Ordinal))
                continue;

            if (line.Contains("懲役", StringComparison.Ordinal)
                || line.Contains("罰金", StringComparison.Ordinal)
                || line.Contains("禁錮", StringComparison.Ordinal)
                || line.Contains("拘留", StringComparison.Ordinal)
                || line.Contains("科料", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }
}
