using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット表示用のリッチブロック分割（段落・リスト・表）。</summary>
public static partial class ChatRichContentParser
{
    public static IReadOnlyList<ChatDisplayBlock> ParseBlocks(string? text, bool sentenceBreaks = true)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatDisplayBlock>();

        var blocks = new List<ChatDisplayBlock>();
        foreach (var segment in ChatFencedCodeExtractor.Split(text))
        {
            if (segment.IsCode)
            {
                blocks.Add(new ChatDisplayBlock
                {
                    Kind = ChatDisplayBlockKind.Code,
                    CodeText = ChatCodeLayoutNormalizer.FormatForDisplay(segment.Text),
                });
                continue;
            }

            blocks.AddRange(ParseProseBlocks(segment.Text, sentenceBreaks));
        }

        return blocks.Count > 0 ? blocks : Array.Empty<ChatDisplayBlock>();
    }

    private static IReadOnlyList<ChatDisplayBlock> ParseProseBlocks(string? text, bool sentenceBreaks)
    {
        var formatted = ChatDisplayFormatter.FormatForDisplay(text, sentenceBreaks);
        if (string.IsNullOrEmpty(formatted))
            return Array.Empty<ChatDisplayBlock>();

        var normalized = ChatMarkdownLayoutNormalizer.Normalize(formatted);
        var lines = normalized
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !ChatMarkdownLayoutNormalizer.IsHorizontalRuleLine(l))
            .ToList();

        lines = PromoteVerticalTableHeaders(lines);
        lines = ConvertVerticalPipeCellBlocks(lines);

        if (lines.Count == 0)
            return Array.Empty<ChatDisplayBlock>();

        var blocks = ParseLineGroups(lines).ToList();
        if (blocks.Count == 0)
        {
            blocks.Add(new ChatDisplayBlock
            {
                Kind = ChatDisplayBlockKind.Paragraph,
                ParagraphLines = new[] { normalized },
            });
        }

        return blocks;
    }

    /// <summary>
    /// モデルが返しがちな「1行1セル」形式（| 項目A … のみ）を通常の表行に変換する。
    /// </summary>
    private static List<string> ConvertVerticalPipeCellBlocks(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            if (!IsVerticalPipeBlockLine(lines[i]))
            {
                result.Add(lines[i++]);
                continue;
            }

            var block = new List<string>();
            while (i < lines.Count && IsVerticalPipeBlockLine(lines[i]))
                block.Add(lines[i++]);

            if (TryConvertVerticalPipeBlock(block, out var tableRows))
                result.AddRange(tableRows);
            else
                result.AddRange(block);
        }

        return result;
    }

    private static bool TryConvertVerticalPipeBlock(
        IReadOnlyList<string> block,
        out List<string> tableRows)
    {
        tableRows = new List<string>();

        var cells = new List<string>();
        foreach (var line in block)
        {
            if (!TryParseVerticalPipeCell(line, out var cell))
                continue;

            if (IsJunkTableCell(cell))
                continue;

            cells.Add(cell);
        }

        if (cells.Count < 2)
            return false;

        var columnCount = DetectVerticalPipeColumnCount(block, cells);
        if (columnCount < 2)
            return false;

        for (var offset = 0; offset < cells.Count; offset += columnCount)
        {
            var rowCells = cells.Skip(offset).Take(columnCount).ToList();
            if (rowCells.All(string.IsNullOrWhiteSpace))
                continue;

            while (rowCells.Count < columnCount)
                rowCells.Add(string.Empty);

            tableRows.Add(BuildTableRow(rowCells));
        }

        return tableRows.Count > 0
               && tableRows.Any(r => ParseHorizontalTableRow(r) is { Count: >= 2 });
    }

    /// <summary>
    /// ヘッダー3行 + |: 区切り3行 … のような並びから列数を推定する。
    /// </summary>
    private static int DetectVerticalPipeColumnCount(
        IReadOnlyList<string> block,
        IReadOnlyList<string> extractedCells)
    {
        var cellsBeforeSeparator = 0;
        var inSeparator = false;
        var separatorRun = 0;
        var headerCandidate = 0;

        foreach (var line in block)
        {
            if (!TryParseVerticalPipeCell(line, out var cell))
                continue;

            if (IsJunkTableCell(cell))
            {
                if (cellsBeforeSeparator >= 2 && !inSeparator)
                {
                    inSeparator = true;
                    separatorRun = 1;
                    headerCandidate = cellsBeforeSeparator;
                }
                else if (inSeparator)
                {
                    separatorRun++;
                }
            }
            else
            {
                if (inSeparator && separatorRun >= 2)
                    return Math.Max(headerCandidate, separatorRun);

                inSeparator = false;
                separatorRun = 0;
                cellsBeforeSeparator++;
            }
        }

        if (headerCandidate >= 2)
            return headerCandidate;

        return GuessColumnCount(extractedCells);
    }

    private static int GuessColumnCount(IReadOnlyList<string> cells)
    {
        ReadOnlySpan<int> preferences = [3, 2, 4, 5, 6];
        foreach (var n in preferences)
        {
            if (cells.Count >= n && cells.Count % n == 0)
                return n;
        }

        foreach (var n in preferences)
        {
            if (cells.Count >= n * 2)
                return n;
        }

        return cells.Count >= 2 ? 2 : 0;
    }

    private static bool IsVerticalPipeBlockLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|'))
            return false;

        return !IsHorizontalTableRow(line) && TryParseVerticalPipeCell(line, out _);
    }

    private static bool IsHorizontalTableRow(string line)
    {
        var row = ParseHorizontalTableRow(line);
        return row is { Count: >= 2 };
    }

    private static bool TryParseVerticalPipeCell(string line, out string cell)
    {
        cell = string.Empty;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|'))
            return false;

        var inner = trimmed.TrimStart('|').TrimEnd('|').Trim();
        if (inner.Contains('|'))
            return false;

        cell = inner;
        return true;
    }

    private static List<string> PromoteVerticalTableHeaders(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            if (!IsPotentialVerticalHeaderLine(lines[i]))
            {
                result.Add(lines[i++]);
                continue;
            }

            var headers = new List<string>();
            while (i < lines.Count && IsPotentialVerticalHeaderLine(lines[i]))
                headers.Add(lines[i++]);

            var probe = i;
            while (probe < lines.Count && ChatMarkdownLayoutNormalizer.IsTableNoiseLine(lines[probe]))
                probe++;

            if (headers.Count >= 2 && probe < lines.Count && IsTableContentLine(lines[probe]))
            {
                result.Add(BuildTableRow(headers));
                continue;
            }

            result.AddRange(headers);
        }

        return result;
    }

    private static IEnumerable<ChatDisplayBlock> ParseLineGroups(IReadOnlyList<string> lines)
    {
        var index = 0;
        while (index < lines.Count)
        {
            if (ChatMarkdownLayoutNormalizer.IsTableNoiseLine(lines[index]))
            {
                index++;
                continue;
            }

            if (IsVerticalPipeBlockLine(lines[index]))
            {
                var group = new List<string>();
                while (index < lines.Count && IsVerticalPipeBlockLine(lines[index]))
                    group.Add(lines[index++]);

                if (TryConvertVerticalPipeBlock(group, out var converted))
                    yield return ParseTable(converted);
                else
                {
                    yield return new ChatDisplayBlock
                    {
                        Kind = ChatDisplayBlockKind.Paragraph,
                        ParagraphLines = group,
                    };
                }

                continue;
            }

            if (IsTableLine(lines[index]))
            {
                var group = new List<string>();
                while (index < lines.Count)
                {
                    if (ChatMarkdownLayoutNormalizer.IsTableNoiseLine(lines[index]))
                    {
                        index++;
                        continue;
                    }

                    if (!IsTableLine(lines[index]))
                        break;

                    group.Add(lines[index++]);
                }

                if (group.Count > 0)
                    yield return ParseTable(group);
            }
            else if (IsListLine(lines[index]))
            {
                var group = new List<string>();
                while (index < lines.Count && IsListLine(lines[index]))
                    group.Add(lines[index++]);
                yield return ParseList(group);
            }
            else
            {
                var group = new List<string>();
                while (index < lines.Count
                       && !ChatMarkdownLayoutNormalizer.IsTableNoiseLine(lines[index])
                       && !IsVerticalPipeBlockLine(lines[index])
                       && !IsTableLine(lines[index])
                       && !IsListLine(lines[index]))
                {
                    var cleaned = ChatMarkdownLayoutNormalizer.StripHeadingPrefix(lines[index]);
                    if (cleaned.Length > 0)
                        group.Add(cleaned);
                    index++;
                }

                if (group.Count > 0)
                {
                    yield return new ChatDisplayBlock
                    {
                        Kind = ChatDisplayBlockKind.Paragraph,
                        ParagraphLines = group,
                    };
                }
            }
        }
    }

    private static ChatDisplayBlock ParseList(IReadOnlyList<string> lines)
    {
        var ordered = OrderedListLineRegex().IsMatch(lines[0]);
        var items = lines
            .Select(line => ListMarkerRegex().Replace(line, string.Empty).Trim())
            .Where(item => item.Length > 0)
            .ToList();

        return new ChatDisplayBlock
        {
            Kind = ChatDisplayBlockKind.List,
            ListOrdered = ordered,
            ListItems = items,
        };
    }

    private static ChatDisplayBlock ParseTable(IReadOnlyList<string> lines)
    {
        var rows = lines
            .Select(ParseHorizontalTableRow)
            .Where(r => r is not null)
            .Cast<IReadOnlyList<string>>()
            .ToList();

        if (rows.Count == 0 || !IsValidTable(rows))
        {
            return new ChatDisplayBlock
            {
                Kind = ChatDisplayBlockKind.Paragraph,
                ParagraphLines = lines.ToList(),
            };
        }

        var maxCols = rows.Max(r => r.Count);
        var normalizedRows = rows.Select(r => PadRow(r, maxCols)).ToList();

        var header = normalizedRows[0].ToList();
        var body = normalizedRows.Skip(1).ToList();
        if (body.Count > 0 && IsTableSeparatorRow(body[0]))
            body = body.Skip(1).ToList();

        return new ChatDisplayBlock
        {
            Kind = ChatDisplayBlockKind.Table,
            TableHeader = header,
            TableRows = body,
        };
    }

    private static bool IsListLine(string line) => ListLineRegex().IsMatch(line);

    private static bool IsTableLine(string line) => ParseHorizontalTableRow(line) is not null;

    private static bool IsTableContentLine(string line)
    {
        var cells = ParseHorizontalTableRow(line);
        if (cells is null)
            return false;

        if (IsTableSeparatorRow(cells))
            return true;

        return cells.Count >= 2;
    }

    private static bool IsPotentialVerticalHeaderLine(string line)
    {
        if (IsListLine(line) || IsTableLine(line) || IsVerticalPipeBlockLine(line))
            return false;

        if (ChatMarkdownLayoutNormalizer.IsHorizontalRuleLine(line)
            || ChatMarkdownLayoutNormalizer.IsTableNoiseLine(line))
            return false;

        var trimmed = line.Trim();
        return trimmed.Length is >= 1 and <= 120 && !trimmed.Contains('|');
    }

    private static string BuildTableRow(IReadOnlyList<string> cells) =>
        "| " + string.Join(" | ", cells.Select(c => c.Trim())) + " |";

    private static IReadOnlyList<string> PadRow(IReadOnlyList<string> row, int columnCount)
    {
        if (row.Count >= columnCount)
            return row;

        var padded = row.ToList();
        while (padded.Count < columnCount)
            padded.Add(string.Empty);
        return padded;
    }

    private static IReadOnlyList<string>? ParseHorizontalTableRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|'))
            return null;

        if (!trimmed.EndsWith('|'))
        {
            if (trimmed.Count(c => c == '|') < 2)
                return null;
            trimmed += '|';
        }

        if (trimmed.Length < 3)
            return null;

        var inner = trimmed[1..^1];
        if (inner.Length == 0)
            return null;

        if (!inner.Contains('|'))
            return null;

        var cells = inner
            .Split('|')
            .Select(c => c.Trim())
            .ToList();

        if (cells.Count == 0)
            return null;

        if (IsTableSeparatorRow(cells))
            return cells;

        if (IsJunkOnlyRow(cells))
            return null;

        return cells.Count >= 2 ? cells : null;
    }

    private static bool IsValidTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
            return false;

        var maxCols = rows.Max(r => r.Count);
        if (maxCols < 2)
            return false;

        if (rows.Count >= 2)
            return true;

        return rows[0].Count >= 2 && !IsTableSeparatorRow(rows[0]);
    }

    private static bool IsJunkOnlyRow(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(IsJunkTableCell);

    private static bool IsJunkTableCell(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
            return true;

        return JunkTableCellRegex().IsMatch(cell.Trim());
    }

    private static bool IsTableSeparatorRow(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(c => TableSeparatorCellRegex().IsMatch(c));

    [GeneratedRegex(@"^\s*[-*•]\s+|\s*\d+\.\s+")]
    private static partial Regex ListLineRegex();

    [GeneratedRegex(@"^\s*\d+\.\s*")]
    private static partial Regex OrderedListLineRegex();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+\.)\s*")]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^:?-{3,}:?$")]
    private static partial Regex TableSeparatorCellRegex();

    [GeneratedRegex(@"^[-:|]+$|^:?-{1,2}:?$")]
    private static partial Regex JunkTableCellRegex();
}
