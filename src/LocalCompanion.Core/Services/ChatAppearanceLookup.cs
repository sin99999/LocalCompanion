using System.Text.RegularExpressions;
using LocalCompanion.Models;
using System.IO;

namespace LocalCompanion.Services;

internal sealed record CharacterAppearancePreset(string FileName, CharacterProfileDto Profile);

/// <summary>
/// 「〇〇の見た目で」の対象名を取り、登録キャラクター JSON と照合し、容姿テキストを抜く。
/// ネット検索や添付への接続は <see cref="ChatAppearanceLookupEnricher"/>。
/// </summary>
internal static class ChatAppearanceLookup
{
    private static readonly Regex JapaneseCue = new(
        @"(?<name>[^、。,\n]{1,40}?)の(?:見た目|容姿|外見)(?:で|を|情報|プロンプト)",
        RegexOptions.Compiled);

    private static readonly Regex EnglishCue = new(
        @"(?:appearance\s+of\s+(?<name>[\w][\w-]{0,38})|(?<name>[\w][\w-]{0,38})(?:'s)\s+appearance)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NextHeading = new(
        @"^(?:#{1,3}\s+\S+|【[^】]{1,40}】)",
        RegexOptions.Compiled);

    private static readonly string[] Honorifics = ["ちゃん", "さん", "くん", "様", "氏", "たん"];

    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "今日", "今", "普通", "いつもの", "その", "あの", "この", "同じ",
        "自分", "私", "俺", "僕", "あなた", "you", "me", "my",
    };

    public static IReadOnlyList<string> ExtractSubjectNames(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in JapaneseCue.Matches(message))
            TryAddName(names, seen, match.Groups["name"].Value);
        foreach (Match match in EnglishCue.Matches(message))
            TryAddName(names, seen, match.Groups["name"].Value);
        return names;
    }

    public static bool LooksLikeAppearanceRequest(string? message) =>
        ExtractSubjectNames(message).Count > 0;

    public static string NormalizeName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0)
            return "";

        foreach (var honorific in Honorifics)
        {
            if (name.Length > honorific.Length
                && name.EndsWith(honorific, StringComparison.Ordinal))
            {
                name = name[..^honorific.Length].Trim();
                break;
            }
        }

        return name;
    }

    public static CharacterProfileDto? Match(
        string subject,
        IReadOnlyList<CharacterAppearancePreset> presets)
    {
        var keys = SubjectKeys(subject);
        CharacterProfileDto? found = null;
        var bestLen = -1;
        foreach (var key in keys)
        {
            if (key.Length < 2)
                continue;

            foreach (var preset in presets)
            {
                var fileStem = Path.GetFileNameWithoutExtension(preset.FileName);
                if (!NameEquals(preset.Profile.Name, key) && !NameEquals(fileStem, key))
                    continue;
                var score = Math.Max(preset.Profile.Name.Length, fileStem.Length);
                if (score > bestLen)
                {
                    found = preset.Profile;
                    bestLen = score;
                }
            }
        }

        return found;
    }

    public static string ExtractAppearanceText(string? persona)
    {
        var text = (persona ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
            return "";

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsAppearanceHeading(lines[i]))
                continue;

            var body = new List<string>();
            var sameLine = StripAppearanceHeading(lines[i]);
            if (!string.IsNullOrWhiteSpace(sameLine))
                body.Add(sameLine);

            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j];
                if (j > i + 1 && NextHeading.IsMatch(line.Trim()) && !IsAppearanceHeading(line))
                    break;
                if (IsAppearanceHeading(line) && j > i)
                    break;
                body.Add(line);
            }

            var extracted = string.Join("\n", body).Trim();
            return Clamp(extracted.Length > 0 ? extracted : text);
        }

        return Clamp(text);
    }

    public static string BuildWebQuery(string subject)
    {
        var name = NormalizeName(LastNameSegment(subject));
        if (name.Length == 0)
            name = NormalizeName(subject);
        return TextScriptHelper.LooksJapanese(name)
            ? $"{name} 外見 容姿 髪"
            : $"{name} character appearance hair eyes";
    }

    private static void TryAddName(List<string> names, HashSet<string> seen, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return;
        var key = NormalizeName(LastNameSegment(trimmed));
        if (key.Length < 2 || SkipNames.Contains(key) || SkipNames.Contains(trimmed))
            return;
        if (!seen.Add(key))
            return;
        names.Add(trimmed);
    }

    private static IReadOnlyList<string> SubjectKeys(string subject)
    {
        var full = NormalizeName(subject);
        var last = NormalizeName(LastNameSegment(subject));
        if (string.Equals(full, last, StringComparison.Ordinal))
            return [full];
        return [last, full];
    }

    private static string LastNameSegment(string subject)
    {
        var parts = subject.Split('の', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? subject.Trim() : parts[^1];
    }

    private static bool NameEquals(string? presetName, string key)
    {
        var left = NormalizeName(presetName);
        return left.Length >= 2
            && string.Equals(left, key, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppearanceHeading(string line)
    {
        var t = line.Trim();
        if (t.Length == 0)
            return false;
        t = t.TrimStart('#', ' ', '　');
        t = t.Trim('[', ']', '【', '】');
        return t.StartsWith("容姿", StringComparison.Ordinal)
            || t.StartsWith("外見", StringComparison.Ordinal)
            || t.StartsWith("見た目", StringComparison.Ordinal)
            || t.StartsWith("appearance", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("looks", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripAppearanceHeading(string line)
    {
        var t = line.Trim();
        t = Regex.Replace(t, @"^#{1,3}\s*", "");
        t = Regex.Replace(t, @"^【(?:容姿|外見|見た目)】\s*", "");
        t = Regex.Replace(t, @"^\[(?:appearance|looks)\]\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^(?:容姿|外見|見た目)\s*[:：]?\s*", "");
        t = Regex.Replace(t, @"^(?:appearance|looks)\s*[:：]?\s*", "", RegexOptions.IgnoreCase);
        return t.Trim();
    }

    private static string Clamp(string text)
    {
        const int max = 1500;
        var t = text.Trim();
        if (t.Length <= max)
            return t;
        return t[..max] + "…";
    }
}
