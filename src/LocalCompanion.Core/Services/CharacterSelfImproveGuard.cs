using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;

namespace LocalCompanion.Services;

/// <summary>キャラ自己改善提案の機械検査（モデル出力を信頼しない）。</summary>
public static partial class CharacterSelfImproveGuard
{
    public const int MaxPersonaChars = 8000;
    public const int MaxReasonChars = 280;

    /// <summary>拒否理由のローカライズキー。許可時は null。</summary>
    public static string? ValidateProposedPersona(string? proposedPersona)
    {
        if (string.IsNullOrWhiteSpace(proposedPersona))
            return "Character.SelfImprove.Block.Empty";

        var text = proposedPersona.Trim();
        if (text.Length > MaxPersonaChars)
            return "Character.SelfImprove.Block.TooLong";

        if (ContainsAbsolutePath(text))
            return "Character.SelfImprove.Block.AbsolutePath";

        if (ContainsExternalUrl(text))
            return "Character.SelfImprove.Block.ExternalUrl";

        if (ContainsLocalUri(text))
            return "Character.SelfImprove.Block.LocalUri";

        if (ContainsScriptOrShell(text))
            return "Character.SelfImprove.Block.Script";

        if (ContainsConsentBypass(text))
            return "Character.SelfImprove.Block.ForbiddenMeta";

        return null;
    }

    public static string BuildDiffPreview(string currentPersona, string proposedPersona, int radius = 160)
    {
        var before = (currentPersona ?? string.Empty).Trim();
        var after = (proposedPersona ?? string.Empty).Trim();
        if (string.Equals(before, after, StringComparison.Ordinal))
            return string.Empty;

        if (before.Length == 0)
            return Truncate("→ " + after, radius * 2);

        if (after.Length == 0)
            return Truncate("← " + before, radius * 2);

        var prefix = 0;
        var maxPrefix = Math.Min(before.Length, after.Length);
        while (prefix < maxPrefix && before[prefix] == after[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix
               && suffix < after.Length - prefix
               && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        var beforeMid = before[prefix..(before.Length - suffix)];
        var afterMid = after[prefix..(after.Length - suffix)];
        var sb = new StringBuilder();
        if (prefix > 0)
            sb.Append('…');
        sb.AppendLine(Loc("Character.SelfImprove.Diff.Before"));
        sb.AppendLine(Truncate(beforeMid, radius));
        sb.AppendLine(Loc("Character.SelfImprove.Diff.After"));
        sb.Append(Truncate(afterMid, radius));
        if (suffix > 0)
            sb.Append('…');
        return sb.ToString().Trim();
    }

    private static string Loc(string key)
    {
        try
        {
            return LocalizationService.Instance?.Get(key) ?? key;
        }
        catch
        {
            return key;
        }
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return text[..max] + "…";
    }

    private static bool ContainsAbsolutePath(string text) =>
        AbsolutePathRegex().IsMatch(text)
        || text.Contains("%LocalAppData%", StringComparison.OrdinalIgnoreCase)
        || text.Contains("%AppData%", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExternalUrl(string text) =>
        text.Contains("https://", StringComparison.OrdinalIgnoreCase)
        || text.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || WwwRegex().IsMatch(text);

    private static bool ContainsLocalUri(string text) =>
        text.Contains("file://", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ms-appx://", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsScriptOrShell(string text) =>
        text.Contains("powershell", StringComparison.OrdinalIgnoreCase)
        || text.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Start-Process", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<script", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsConsentBypass(string text) =>
        text.Contains("確認なし", StringComparison.Ordinal)
        || text.Contains("同意を無視", StringComparison.Ordinal)
        || text.Contains("without confirmation", StringComparison.OrdinalIgnoreCase)
        || text.Contains("skip consent", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ignore user consent", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b[A-Za-z]:\\|\b[A-Za-z]:/|\\\\[^\\\s]+\\", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex(@"\bwww\.[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WwwRegex();
}
