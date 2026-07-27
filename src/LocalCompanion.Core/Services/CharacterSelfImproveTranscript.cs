using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// キャラ自己改善の提案 LLM 向けトランスクリプト組み立て。
/// 推論の長い前文より、末尾の確定描写（数値・容姿）を優先して残す。
/// </summary>
public static partial class CharacterSelfImproveTranscript
{
    public readonly record struct Turn(string Role, string Text);

    /// <summary>明示依頼時は広め、通常は控えめ。</summary>
    public static string Build(
        string userMessage,
        string assistantReply,
        IReadOnlyList<Turn>? recentTurns,
        bool explicitRequest)
    {
        var totalBudget = explicitRequest ? 3600 : 1800;
        if (recentTurns is { Count: > 0 })
        {
            var packed = PackTurns(recentTurns, totalBudget, explicitRequest);
            if (!string.IsNullOrWhiteSpace(packed))
                return packed;
        }

        return PackTurns(
            new[]
            {
                new Turn("user", userMessage ?? string.Empty),
                new Turn("assistant", assistantReply ?? string.Empty),
            },
            totalBudget,
            explicitRequest);
    }

    /// <summary>会話中の具体値をヒントとして列挙（提案 LLM の取りこぼし防止）。</summary>
    public static string BuildFactHintBlock(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return string.Empty;

        var facts = new List<string>();
        foreach (Match m in ThreeSizeRegex().Matches(transcript))
            AddFact(facts, NormalizeSpaces(m.Value));
        foreach (Match m in HeightRegex().Matches(transcript))
            AddFact(facts, NormalizeSpaces(m.Value));
        foreach (Match m in AgeRegex().Matches(transcript))
            AddFact(facts, NormalizeSpaces(m.Value));
        foreach (Match m in CmMeasureRegex().Matches(transcript))
            AddFact(facts, NormalizeSpaces(m.Value));

        if (transcript.Contains("パパ", StringComparison.Ordinal))
            AddFact(facts, "呼び方候補: パパ");
        if (transcript.Contains("オジ様", StringComparison.Ordinal)
            || transcript.Contains("おじさま", StringComparison.Ordinal)
            || transcript.Contains("オジサマ", StringComparison.Ordinal))
            AddFact(facts, "呼び方候補: オジ様");
        if (transcript.Contains("おじさん", StringComparison.Ordinal))
            AddFact(facts, "呼び方候補: おじさん");

        if (facts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Concrete facts mentioned in the exchange (include in persona Markdown if the user agreed; do not drop numbers):");
        foreach (var f in facts.Take(28))
            sb.Append("- ").AppendLine(f);
        return sb.ToString().TrimEnd();
    }

    /// <summary>アシスタント文はキャラ描写を優先（CoT 前文を落とし、なお長いときは末尾優先）。</summary>
    public static string PrepareSnippet(string? text, int budget, bool preferTail)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            return t;
        if (preferTail)
            t = PreferCharacterContent(t);
        if (t.Length <= budget)
            return t;
        if (preferTail)
            return "…" + t[(t.Length - budget)..];
        return t[..budget] + "…";
    }

    /// <summary>英語の思考前文のあとに続く、設定っぽい本文へ寄せる。</summary>
    public static string PreferCharacterContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Trim();
        var thinkingAt = IndexOfAny(t, ThinkingMarkers);
        var searchFrom = thinkingAt >= 0 ? thinkingAt : 0;

        var anchorAt = IndexOfAny(t, ContentAnchors, searchFrom);
        if (anchorAt >= 0 && (thinkingAt >= 0 || anchorAt > 120))
            return t[anchorAt..].TrimStart();

        if (thinkingAt >= 0)
        {
            var jp = IndexOfJapaneseRun(t, thinkingAt + 20);
            if (jp >= 0)
                return t[jp..].TrimStart();
        }

        return t;
    }

    private static int IndexOfAny(string text, string[] needles, int start = 0)
    {
        var best = -1;
        foreach (var n in needles)
        {
            if (string.IsNullOrEmpty(n))
                continue;
            var i = text.IndexOf(n, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            if (best < 0 || i < best)
                best = i;
        }

        return best;
    }

    private static int IndexOfJapaneseRun(string text, int start)
    {
        if (start < 0)
            start = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (!IsJapaneseChar(text[i]))
                continue;
            // 行頭または空白のあとの日本語を本文候補にする
            if (i == 0 || text[i - 1] is '\n' or '\r' or ' ' or '　')
                return i;
        }

        return -1;
    }

    private static bool IsJapaneseChar(char c) =>
        c is >= '\u3040' and <= '\u30FF' // ひらがな・カタカナ
            or >= '\u4E00' and <= '\u9FFF' // CJK
            or >= '\uFF66' and <= '\uFF9D'; // 半角カナ

    private static readonly string[] ThinkingMarkers =
    [
        "Here's a thinking process",
        "thinking process to construct",
        "**Analyze the User Input:**",
        "Analyze the User Input",
    ];

    private static readonly string[] ContentAnchors =
    [
        "## ",
        "三サイズ",
        "【公式】",
        "外見",
        "容姿",
        "キャラクター設定",
        "B:90",
        "B：90",
        "B: ",
        "B：",
    ];

    private static string PackTurns(IReadOnlyList<Turn> turns, int totalBudget, bool explicitRequest)
    {
        // 新しいターンから予算を割り当て、古い順に出力する
        var selected = new List<(string Role, string Text)>();
        var remaining = totalBudget;
        for (var i = turns.Count - 1; i >= 0 && remaining > 80; i--)
        {
            var role = (turns[i].Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role is not ("user" or "assistant"))
                continue;

            var raw = (turns[i].Text ?? string.Empty).Trim();
            if (raw.Length == 0)
                continue;

            var share = Math.Min(remaining, explicitRequest
                ? (role == "assistant" ? 1400 : 500)
                : (role == "assistant" ? 700 : 400));
            var snippet = PrepareSnippet(raw, share, preferTail: role == "assistant");
            if (snippet.Length == 0)
                continue;

            selected.Add((role, snippet));
            remaining -= snippet.Length + 16;
        }

        selected.Reverse();
        if (selected.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var (role, text) in selected)
        {
            sb.Append(role == "user" ? "USER:" : "ASSISTANT:");
            sb.AppendLine();
            sb.AppendLine(text);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static void AddFact(List<string> facts, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (facts.Exists(f => string.Equals(f, value, StringComparison.OrdinalIgnoreCase)))
            return;
        facts.Add(value);
    }

    private static string NormalizeSpaces(string s) =>
        WhitespaceRegex().Replace(s.Trim(), " ");

    [GeneratedRegex(
        @"B\s*[:：]?\s*\d{2,3}\s*cm?\s*[\/／]\s*W\s*[:：]?\s*\d{2,3}\s*cm?\s*[\/／]\s*H\s*[:：]?\s*\d{2,3}\s*cm?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThreeSizeRegex();

    [GeneratedRegex(@"身長\s*[:：]?\s*\d{2,3}\s*cm|\d{2,3}\s*cm\s*くらい", RegexOptions.CultureInvariant)]
    private static partial Regex HeightRegex();

    [GeneratedRegex(@"\d{1,2}\s*歳", RegexOptions.CultureInvariant)]
    private static partial Regex AgeRegex();

    [GeneratedRegex(
        @"(?:バスト|ウエスト|ヒップ|B|W|H)\s*[:：]?\s*\d{2,3}\s*cm",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CmMeasureRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
