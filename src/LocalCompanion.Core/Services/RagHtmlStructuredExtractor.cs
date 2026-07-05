using System.Net;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>HTML を Markdown 風の構造化テキストに変換する（ローカル・ルールベース）。</summary>
internal static class RagHtmlStructuredExtractor
{
    private static readonly Regex BlockTags = new(
        @"<(h[1-6]|p|div|li|br|tr|td|th|blockquote|pre|article|section)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ToStructuredMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var withoutScripts = Regex.Replace(
            html,
            @"<script\b[^>]*>.*?</script>",
            "\n",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        withoutScripts = Regex.Replace(
            withoutScripts,
            @"<style\b[^>]*>.*?</style>",
            "\n",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var withBreaks = BlockTags.Replace(withoutScripts, match =>
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            return tag switch
            {
                "br" => "\n",
                "h1" => "\n\n# ",
                "h2" => "\n\n## ",
                "h3" => "\n\n### ",
                "h4" => "\n\n#### ",
                "h5" => "\n\n##### ",
                "h6" => "\n\n###### ",
                "li" => "\n- ",
                "blockquote" => "\n\n> ",
                "pre" => "\n\n```\n",
                "tr" => "\n",
                "td" or "th" => " | ",
                _ => "\n\n",
            };
        });

        var noTags = Regex.Replace(withBreaks, @"<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        decoded = Regex.Replace(decoded, @"[ \t]+", " ");
        decoded = Regex.Replace(decoded, @"\n{3,}", "\n\n");
        return decoded.Trim();
    }
}
