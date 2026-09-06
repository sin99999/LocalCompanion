using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 直前の返答にある番号／記号の提案を、ユーザーが番号だけ送ったときに本文へ広げる。
/// </summary>
public static partial class ChatChoiceReplyExpander
{
    public static string Expand(string? userMessage, string? lastAssistantMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return userMessage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(lastAssistantMessage))
            return userMessage;

        var trimmed = NormalizeDigits(userMessage.Trim());
        if (!TryParseBareChoice(trimmed, out var number, out var letter))
            return userMessage;

        if (number is > 0)
        {
            var items = ExtractListItems(lastAssistantMessage);
            if (number > items.Count)
                return userMessage;
            return $"{number.Value}. {items[number.Value - 1]}";
        }

        var lettered = ExtractLetteredItems(lastAssistantMessage);
        var key = char.ToUpperInvariant(letter);
        if (!lettered.TryGetValue(key, out var text))
            return userMessage;
        return $"{key}. {text}";
    }

    private static bool TryParseBareChoice(string trimmed, out int? number, out char letter)
    {
        number = null;
        letter = '\0';
        var match = BareChoiceRegex().Match(trimmed);
        if (!match.Success)
            return false;

        var key = match.Groups[1].Value;
        if (key.Length == 1 && char.IsLetter(key[0]))
        {
            letter = key[0];
            return true;
        }

        if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= 1 and <= 99)
        {
            number = n;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractListItems(string assistant)
    {
        var blocks = ChatRichContentParser.ParseBlocks(assistant, sentenceBreaks: true);
        ChatDisplayBlock? lastList = null;
        foreach (var block in blocks)
        {
            if (block.Kind != ChatDisplayBlockKind.List || block.ListItems.Count == 0)
                continue;
            lastList = block;
        }

        if (lastList is not null)
        {
            var fromList = FlattenInlineNumbered(lastList.ListItems);
            if (fromList.Count >= 2)
                return fromList;
        }

        var fromText = SplitInlineNumbered(assistant);
        return fromText.Count >= 2 ? fromText : [];
    }

    private static List<string> FlattenInlineNumbered(IReadOnlyList<string> items)
    {
        var result = new List<string>();
        foreach (var item in items)
        {
            var split = SplitInlineNumbered("1. " + item);
            if (split.Count > 1)
                result.AddRange(split);
            else
                result.Add(item);
        }

        return result;
    }

    private static List<string> SplitInlineNumbered(string text)
    {
        var matches = InlineNumberedRegex().Matches(text);
        if (matches.Count < 2)
            return [];

        var items = new List<string>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var item = text[start..end].Trim();
            if (item.Length > 0)
                items.Add(item);
        }

        return items;
    }

    private static Dictionary<char, string> ExtractLetteredItems(string assistant)
    {
        var found = new Dictionary<char, string>();
        var blocks = ChatRichContentParser.ParseBlocks(assistant, sentenceBreaks: true);
        foreach (var block in blocks)
        {
            if (block.Kind == ChatDisplayBlockKind.Paragraph)
            {
                foreach (var line in block.ParagraphLines)
                    AddLetteredFromText(line, found);
            }
            else if (block.Kind == ChatDisplayBlockKind.List)
            {
                foreach (var item in block.ListItems)
                    AddLetteredFromText(item, found);
            }
        }

        if (found.Count == 0)
            AddLetteredFromText(assistant, found);

        return found;
    }

    private static void AddLetteredFromText(string text, Dictionary<char, string> found)
    {
        var matches = InlineLetteredRegex().Matches(text);
        if (matches.Count == 0)
            return;

        for (var i = 0; i < matches.Count; i++)
        {
            var key = char.ToUpperInvariant(matches[i].Groups[1].Value[0]);
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var item = text[start..end].Trim();
            if (item.Length > 0)
                found[key] = item;
        }
    }

    private static string NormalizeDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '０' and <= '９')
                sb.Append((char)('0' + (c - '０')));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^(\d{1,2}|[A-Za-z])[.．。、)）]?$")]
    private static partial Regex BareChoiceRegex();

    [GeneratedRegex(@"(?<!\d)(\d{1,2})[.．][\s　]+")]
    private static partial Regex InlineNumberedRegex();

    [GeneratedRegex(@"(?<![A-Za-z])([A-Za-z])[.．][\s　]+")]
    private static partial Regex InlineLetteredRegex();
}
