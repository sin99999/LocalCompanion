using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 提案 LLM が propose=false / JSON 破損したときの機械フォールバック。
/// アシスタントが列挙したルール・か条を persona の ## ルール に取り込む。
/// </summary>
public static partial class CharacterSelfImproveFallback
{
    /// <summary>
    /// 返答から箇条・番号付きの短文を抽出し、現行 persona にマージした全文を返す。
    /// 抽出が弱いときは null。
    /// </summary>
    public static string? TryMergeListedRules(string? currentPersona, string? assistantReply)
    {
        var reply = CharacterSelfImproveTranscript.PreferCharacterContent(assistantReply ?? string.Empty);
        if (string.IsNullOrWhiteSpace(reply))
            return null;

        var rules = ExtractRuleLines(reply);
        if (rules.Count < 2)
            return null;

        var current = (currentPersona ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var section = BuildRulesSection(rules);
        var merged = UpsertRulesSection(current, section);
        if (string.IsNullOrWhiteSpace(merged))
            return null;
        if (string.Equals(
                Normalize(current),
                Normalize(merged),
                StringComparison.Ordinal))
            return null;

        return merged.Trim();
    }

    public static IReadOnlyList<string> ExtractRuleLines(string text)
    {
        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var m = NumberedOrBulletRegex().Match(line);
            if (!m.Success)
                continue;

            var body = m.Groups["body"].Value.Trim().TrimStart('　', ' ', ':', '：', '-', '・');
            body = StripMarkdownDecor(body);
            if (body.Length < 4 || body.Length > 240)
                continue;
            if (IsJunkRuleLine(body))
                continue;
            if (lines.Exists(x => string.Equals(x, body, StringComparison.Ordinal)))
                continue;

            lines.Add(body);
            if (lines.Count >= 12)
                break;
        }

        return lines;
    }

    private static string BuildRulesSection(IReadOnlyList<string> rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## ルール");
        foreach (var r in rules)
            sb.Append("- ").AppendLine(r);
        return sb.ToString().TrimEnd();
    }

    private static string UpsertRulesSection(string current, string rulesSection)
    {
        if (string.IsNullOrWhiteSpace(current))
            return rulesSection;

        var replaced = RulesSectionRegex().Replace(current, rulesSection);
        if (!string.Equals(replaced, current, StringComparison.Ordinal))
            return replaced;

        return current.TrimEnd() + "\n\n" + rulesSection;
    }

    private static string StripMarkdownDecor(string body)
    {
        body = body.Trim();
        if (body.StartsWith("**", StringComparison.Ordinal) && body.EndsWith("**", StringComparison.Ordinal) && body.Length > 4)
            body = body[2..^2].Trim();
        return body;
    }

    private static bool IsJunkRuleLine(string body)
    {
        if (body.Contains("Here's a thinking", StringComparison.OrdinalIgnoreCase))
            return true;
        if (body.Contains("thinking process", StringComparison.OrdinalIgnoreCase))
            return true;
        if (body.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    [GeneratedRegex(
        @"^(?:#{1,3}\s*)?(?:\*\*)?(?:[-*•・]|第?[0-9０-９]{1,2}\s*[.．、:)）]|[①②③④⑤⑥⑦⑧⑨⑩]|[一二三四五六七八九十]+[、.．)]|[0-9０-９]{1,2}\s*か条)\s*(?<body>.+?)(?:\*\*)?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberedOrBulletRegex();

    [GeneratedRegex(
        @"^##\s*ルール\s*$[\s\S]*?(?=^##\s|\z)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RulesSectionRegex();
}
