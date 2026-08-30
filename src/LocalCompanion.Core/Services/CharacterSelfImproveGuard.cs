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

    /// <summary>変更行だけを前後／新規／削除で見せる。共通の前後行は省略する。</summary>
    public static string BuildDiffPreview(string currentPersona, string proposedPersona, int maxLinesEach = 40)
    {
        var beforeLines = SplitLines(currentPersona);
        var afterLines = SplitLines(proposedPersona);
        if (beforeLines.Count == afterLines.Count
            && beforeLines.SequenceEqual(afterLines))
            return string.Empty;

        var prefix = 0;
        var maxPrefix = Math.Min(beforeLines.Count, afterLines.Count);
        while (prefix < maxPrefix
               && string.Equals(beforeLines[prefix], afterLines[prefix], StringComparison.Ordinal))
            prefix++;

        var suffix = 0;
        while (suffix < beforeLines.Count - prefix
               && suffix < afterLines.Count - prefix
               && string.Equals(
                   beforeLines[beforeLines.Count - 1 - suffix],
                   afterLines[afterLines.Count - 1 - suffix],
                   StringComparison.Ordinal))
        {
            suffix++;
        }

        var removed = beforeLines.Skip(prefix).Take(beforeLines.Count - prefix - suffix).ToList();
        var added = afterLines.Skip(prefix).Take(afterLines.Count - prefix - suffix).ToList();
        if (removed.Count == 0 && added.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        if (prefix > 0)
            sb.AppendLine("…");

        if (removed.Count == 0)
        {
            sb.AppendLine(Loc("Character.SelfImprove.Diff.New"));
            sb.Append(JoinLines(added, maxLinesEach));
        }
        else if (added.Count == 0)
        {
            sb.AppendLine(Loc("Character.SelfImprove.Diff.Deleted"));
            sb.Append(JoinLines(removed, maxLinesEach));
        }
        else
        {
            sb.AppendLine(Loc("Character.SelfImprove.Diff.Before"));
            sb.AppendLine(JoinLines(removed, maxLinesEach));
            sb.AppendLine(Loc("Character.SelfImprove.Diff.After"));
            sb.Append(JoinLines(added, maxLinesEach));
        }

        if (suffix > 0)
        {
            if (!sb.ToString().EndsWith('\n'))
                sb.AppendLine();
            sb.Append('…');
        }

        return sb.ToString().Trim();
    }

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static l => l.TrimEnd())
            .ToList();
    }

    private static string JoinLines(IReadOnlyList<string> lines, int maxLines)
    {
        if (lines.Count == 0)
            return string.Empty;

        if (lines.Count <= maxLines)
            return string.Join(Environment.NewLine, lines);

        var head = lines.Take(maxLines).ToList();
        return string.Join(Environment.NewLine, head) + Environment.NewLine + "…";
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
