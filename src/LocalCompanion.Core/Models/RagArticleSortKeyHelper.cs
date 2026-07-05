namespace LocalCompanion.Models;

/// <summary>Models から RagArticleQueryParser を参照しないための薄いヘルパー。</summary>
internal static class RagArticleSortKeyHelper
{
    private static readonly System.Text.RegularExpressions.Regex HeaderArticlePattern = new(
        @"^第\s*([0-9０-９]{1,3})\s*条(?:の\s*([0-9０-９]{1,2}))?",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool TryParse(string headerText, out long sortKey)
    {
        sortKey = 0;
        if (string.IsNullOrWhiteSpace(headerText))
            return false;

        var match = HeaderArticlePattern.Match(headerText.Trim());
        if (!match.Success)
            return false;

        var mainDigits = NormalizeDigits(match.Groups[1].Value);
        if (!int.TryParse(mainDigits, out var main) || main <= 0)
            return false;

        var sub = 0;
        if (match.Groups[2].Success)
        {
            var subDigits = NormalizeDigits(match.Groups[2].Value);
            if (!int.TryParse(subDigits, out sub) || sub < 0)
                return false;
        }

        sortKey = main * 100L + sub;
        return true;
    }

    private static string NormalizeDigits(string value) =>
        value.Replace('０', '0').Replace('１', '1').Replace('２', '2').Replace('３', '3')
            .Replace('４', '4').Replace('５', '5').Replace('６', '6').Replace('７', '7')
            .Replace('８', '8').Replace('９', '9');
}
