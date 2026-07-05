using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>用語・見出しを entry_key 列用に正規化する。</summary>
internal static class RagEntryKeyNormalizer
{
    private static readonly Regex MarkdownBold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex NonWord = new(@"[\s　\-—–・・/\\|]+", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var text = value.Trim();
        var bold = MarkdownBold.Match(text);
        if (bold.Success)
            text = bold.Groups[1].Value.Trim();

        text = text.Trim('*', '#', ' ', '　', '「', '」', '『', '』', '(', ')', '（', '）');
        text = NonWord.Replace(text, "");
        return text.ToLowerInvariant();
    }

    public static string NormalizeForLookup(string? value)
    {
        var key = Normalize(value);
        if (key.Length == 0)
            return "";

        var sb = new StringBuilder(key.Length);
        foreach (var ch in key)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
