using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>1行に潰されたコードを表示用に読みやすく整形する（DB・原文は変更しない）。</summary>
public static partial class ChatCodeLayoutNormalizer
{
    public static string FormatForDisplay(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var text = code.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Contains('\n') && text.Split('\n').Length >= 2)
            return text;

        text = MissingIncludeHashRegex().Replace(text, "#include <$1>");
        text = IncludeLineBreakRegex().Replace(text, "$1\n");
        text = SemicolonBreakRegex().Replace(text, ";\n");
        text = OpenBraceBreakRegex().Replace(text, " {\n    ");
        text = CloseBraceBreakRegex().Replace(text, "\n}\n");
        text = CollapseBlankLinesRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"(?<!\w|#)include\s*<([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex MissingIncludeHashRegex();

    [GeneratedRegex(@"(#include\s+[^\n;]+;?)", RegexOptions.IgnoreCase)]
    private static partial Regex IncludeLineBreakRegex();

    [GeneratedRegex(@";(?=\S)")]
    private static partial Regex SemicolonBreakRegex();

    [GeneratedRegex(@"\{\s*")]
    private static partial Regex OpenBraceBreakRegex();

    [GeneratedRegex(@"\}(?=\S)")]
    private static partial Regex CloseBraceBreakRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex CollapseBlankLinesRegex();
}
