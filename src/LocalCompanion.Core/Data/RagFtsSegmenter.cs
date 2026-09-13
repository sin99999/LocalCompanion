using System.Text;

namespace LocalCompanion.Data;

/// <summary>
/// RAG の FTS5 用に日本語を空白区切りへ直す。
/// unicode61 は漢字・かなの連続を 1 トークンにするため、漢字列・カタカナ列は文字 2 連（バイグラム）に割る。
/// ひらがな列（助詞・活用）は捨て、英数字は単語のまま残す。索引側と質問側で同じ関数を使う。
/// </summary>
public static class RagFtsSegmenter
{
    /// <summary>本文を索引用のトークン列（空白区切り）にする。</summary>
    public static string Segment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length * 2);
        foreach (var run in SplitRuns(text))
        {
            foreach (var token in TokensOf(run))
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(token);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 質問を FTS5 の MATCH 式にする。1 つの語（漢字列・カタカナ列・英単語）は連続バイグラムのフレーズ、
    /// 語どうしは OR でつなぐ。空なら "" を返す。
    /// </summary>
    public static string BuildMatchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        var phrases = new List<string>();
        var weak = new List<string>();
        foreach (var run in SplitRuns(query))
        {
            var tokens = TokensOf(run).ToList();
            if (tokens.Count == 0)
                continue;

            var phrase = "\"" + string.Join(' ', tokens) + "\"";
            // 1 文字の漢字や数字だけの語（第・条・8）は、他に語があるなら捨てる（条番号は呼び出し側が見出し句で足す）
            var target = run.Text.Length == 1 || run.Text.All(char.IsDigit) ? weak : phrases;
            if (!target.Contains(phrase, StringComparer.Ordinal))
                target.Add(phrase);
            if (phrases.Count >= 16)
                break;
        }

        if (phrases.Count == 0)
            phrases = weak;

        return string.Join(" OR ", phrases);
    }

    /// <summary>質問から「内容語」の連なりだけを返す（漢字列・カタカナ列・英数字、2 文字以上。重複なし）。根拠判定に使う。</summary>
    public static IReadOnlyList<string> ContentRuns(string? text)
    {
        var list = new List<string>();
        foreach (var run in EnumerateContentRuns(text))
        {
            if (!list.Contains(run, StringComparer.OrdinalIgnoreCase))
                list.Add(run);
        }

        return list;
    }

    /// <summary>内容語の連なりを出現順にすべて返す（重複あり。頻度を数える用）。</summary>
    public static IEnumerable<string> EnumerateContentRuns(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var run in SplitRuns(text))
        {
            if (run.Kind == RunKind.Hiragana || run.Text.Length < 2)
                continue;
            yield return run.Text;
        }
    }

    private static IEnumerable<string> TokensOf(Run run)
    {
        switch (run.Kind)
        {
            case RunKind.Latin:
                yield return run.Text;
                yield break;
            case RunKind.Hiragana:
                yield break;
        }

        var text = run.Text;
        if (text.Length == 1)
        {
            yield return text;
            yield break;
        }

        for (var i = 0; i + 1 < text.Length; i++)
            yield return text.Substring(i, 2);
    }

    private static IEnumerable<Run> SplitRuns(string text)
    {
        var sb = new StringBuilder();
        var current = RunKind.None;
        foreach (var ch in text)
        {
            var kind = KindOf(ch);
            if (kind == RunKind.None)
            {
                if (sb.Length > 0)
                    yield return new Run(current, sb.ToString());
                sb.Clear();
                current = RunKind.None;
                continue;
            }

            if (kind != current && sb.Length > 0)
            {
                yield return new Run(current, sb.ToString());
                sb.Clear();
            }

            current = kind;
            sb.Append(ch);
        }

        if (sb.Length > 0)
            yield return new Run(current, sb.ToString());
    }

    private static RunKind KindOf(char ch)
    {
        if (ch is >= '\u3041' and <= '\u309F')
            return RunKind.Hiragana;
        if (ch is >= '\u30A0' and <= '\u30FF' or >= '\u31F0' and <= '\u31FF' or >= '\uFF66' and <= '\uFF9F')
            return ch == '\u30FB' ? RunKind.None : RunKind.Katakana;
        if (ch is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' or '\u3005')
            return RunKind.Kanji;
        if (char.IsLetterOrDigit(ch))
            return RunKind.Latin;
        return RunKind.None;
    }

    private enum RunKind
    {
        None,
        Latin,
        Hiragana,
        Katakana,
        Kanji,
    }

    private readonly record struct Run(RunKind Kind, string Text);
}
