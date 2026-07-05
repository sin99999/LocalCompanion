using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>全形式共通の軽量テキスト正規化（ingest 前）。</summary>
internal static class RagDocumentNormalizer
{
    private static readonly Regex ExcessiveBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingSpaces = new(@"[ \t]+$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingSpaces.Replace(text, "");
        text = ExcessiveBlankLines.Replace(text, "\n\n");
        return text.Trim();
    }
}
