using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>Markdown の ``` フェンスで囲まれたコードブロックを prose から分離する。</summary>
public static partial class ChatFencedCodeExtractor
{
    public readonly record struct Segment(string Text, bool IsCode, string? Language);

    public static IReadOnlyList<Segment> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<Segment>();

        var source = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var matches = FencedCodeRegex().Matches(source);
        if (matches.Count == 0)
            return new[] { new Segment(source, false, null) };

        var segments = new List<Segment>();
        var index = 0;
        foreach (Match match in matches)
        {
            if (match.Index > index)
                segments.Add(new Segment(source[index..match.Index], false, null));

            var language = match.Groups[1].Value.Trim();
            var code = match.Groups[2].Value.Trim('\n');
            segments.Add(new Segment(
                code,
                true,
                string.IsNullOrWhiteSpace(language) ? null : language));
            index = match.Index + match.Length;
        }

        if (index < source.Length)
            segments.Add(new Segment(source[index..], false, null));

        return segments;
    }

    [GeneratedRegex(@"```(\w+)?[^\S\r\n]*\n?(.*?)```", RegexOptions.Singleline)]
    private static partial Regex FencedCodeRegex();
}
