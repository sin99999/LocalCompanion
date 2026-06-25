using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>
/// 単一 TextBlock 向けの枠付き表（罫線文字）。セル折り返しと列幅上限付き。
/// </summary>
public static partial class ChatTableBoxFormatter
{
    private const int CellPad = 1;
    private const int MinColumnWidth = 6;
    private const int MaxColumnWidth = 28;
    private const int DefaultAvailableChars = 72;

    public static IReadOnlyList<string> Format(
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows,
        double availableWidthDip = 0,
        double monospaceFontSize = 12)
    {
        if (header.Count == 0)
            return Array.Empty<string>();

        var sanitizedHeader = header.Select(SanitizeCell).ToList();
        var sanitizedRows = rows
            .Select(r => r.Select(SanitizeCell).ToList())
            .ToList();

        var allRows = new List<IReadOnlyList<string>> { sanitizedHeader };
        allRows.AddRange(sanitizedRows);
        var columnCount = allRows.Max(r => r.Count);
        if (columnCount == 0)
            return Array.Empty<string>();

        var availableChars = EstimateAvailableChars(availableWidthDip, monospaceFontSize, columnCount);
        var columnWidths = ComputeColumnWidths(allRows, columnCount, availableChars);

        var wrappedHeader = WrapRowCells(sanitizedHeader, columnCount, columnWidths);
        var wrappedRows = sanitizedRows
            .Select(r => WrapRowCells(r, columnCount, columnWidths))
            .ToList();

        var lines = new List<string> { BorderRow(columnWidths, '┌', '┬', '┐') };
        lines.AddRange(RenderDataRow(wrappedHeader, columnWidths));
        lines.Add(BorderRow(columnWidths, '├', '┼', '┤'));
        foreach (var wrapped in wrappedRows)
            lines.AddRange(RenderDataRow(wrapped, columnWidths));
        lines.Add(BorderRow(columnWidths, '└', '┴', '┘'));
        return lines;
    }

    public static string SanitizeCell(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
            return string.Empty;

        var text = cell.Trim();
        text = BoldMarkdownRegex().Replace(text, "$1");
        text = ItalicMarkdownRegex().Replace(text, "$1");
        if (text.Length > 2
            && char.IsAsciiDigit(text[0])
            && char.IsAsciiDigit(text[^1])
            && !char.IsAsciiDigit(text[1]))
        {
            text = text[1..^1].Trim();
        }

        return text.Trim();
    }

    private static int EstimateAvailableChars(double availableWidthDip, double fontSize, int columnCount)
    {
        if (availableWidthDip <= 0)
            return DefaultAvailableChars;

        // 等幅フォントのおおよその文字幅（DIP）
        var charWidth = Math.Max(6.0, fontSize * 0.62);
        var usable = Math.Max(24, availableWidthDip - 16);
        var chars = (int)(usable / charWidth * 0.9);
        var borderOverhead = (columnCount + 1) + columnCount * CellPad * 2;
        return Math.Max(columnCount * MinColumnWidth + borderOverhead, chars);
    }

    private static int[] ComputeColumnWidths(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int columnCount,
        int availableChars)
    {
        var natural = new int[columnCount];
        foreach (var row in rows)
        {
            for (var c = 0; c < columnCount; c++)
            {
                var cell = c < row.Count ? row[c] : string.Empty;
                foreach (var line in WrapText(cell, MaxColumnWidth))
                    natural[c] = Math.Max(natural[c], GetDisplayWidth(line));
            }
        }

        for (var c = 0; c < columnCount; c++)
            natural[c] = Math.Clamp(natural[c], MinColumnWidth, MaxColumnWidth);

        var borderOverhead = (columnCount + 1) + columnCount * CellPad * 2;
        var total = borderOverhead + natural.Sum();
        if (total <= availableChars)
            return natural;

        var budget = Math.Max(columnCount * MinColumnWidth, availableChars - borderOverhead);
        var sumNatural = natural.Sum();
        var widths = new int[columnCount];
        var used = 0;
        for (var c = 0; c < columnCount; c++)
        {
            if (c == columnCount - 1)
            {
                widths[c] = Math.Clamp(budget - used, MinColumnWidth, MaxColumnWidth);
                break;
            }

            var share = Math.Max(MinColumnWidth, (int)Math.Floor(budget * (natural[c] / (double)sumNatural)));
            widths[c] = Math.Clamp(share, MinColumnWidth, MaxColumnWidth);
            used += widths[c];
        }

        return widths;
    }

    private static List<IReadOnlyList<string>> WrapRowCells(
        IReadOnlyList<string> cells,
        int columnCount,
        IReadOnlyList<int> columnWidths)
    {
        var wrapped = new List<IReadOnlyList<string>>(columnCount);
        for (var c = 0; c < columnCount; c++)
        {
            var cell = c < cells.Count ? cells[c] : string.Empty;
            wrapped.Add(WrapText(cell, columnWidths[c]));
        }

        return wrapped;
    }

    private static IEnumerable<string> RenderDataRow(
        IReadOnlyList<IReadOnlyList<string>> wrappedCells,
        IReadOnlyList<int> columnWidths)
    {
        var rowHeight = wrappedCells.Count > 0 ? wrappedCells.Max(c => c.Count) : 1;
        if (rowHeight == 0)
            rowHeight = 1;

        for (var lineIndex = 0; lineIndex < rowHeight; lineIndex++)
        {
            var parts = new string[columnWidths.Count];
            for (var c = 0; c < columnWidths.Count; c++)
            {
                var lines = c < wrappedCells.Count ? wrappedCells[c] : Array.Empty<string>();
                var text = lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
                parts[c] = FormatCellLine(text, columnWidths[c]);
            }

            yield return "│" + string.Join("│", parts) + "│";
        }
    }

    private static string FormatCellLine(string text, int columnInnerWidth)
    {
        var inner = new string(' ', CellPad) + text + new string(' ', CellPad);
        var pad = columnInnerWidth + CellPad * 2 - GetDisplayWidth(inner);
        return inner + new string(' ', Math.Max(0, pad));
    }

    private static string BorderRow(IReadOnlyList<int> columnWidths, char left, char junction, char right)
    {
        var segments = columnWidths
            .Select(w => new string('─', w + CellPad * 2))
            .ToList();
        return left + string.Join(junction.ToString(), segments) + right;
    }

    private static List<string> WrapText(string text, int maxDisplayWidth)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string> { string.Empty };

        if (GetDisplayWidth(text) <= maxDisplayWidth)
            return new List<string> { text };

        var lines = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;

        foreach (var ch in text)
        {
            var chWidth = GetCharDisplayWidth(ch);
            if (currentWidth + chWidth > maxDisplayWidth && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            current.Append(ch);
            currentWidth += chWidth;
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return lines.Count > 0 ? lines : new List<string> { string.Empty };
    }

    private static int GetDisplayWidth(string text)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                width += 1;
                i++;
                continue;
            }

            var ch = text[i];
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            width += GetCharDisplayWidth(ch);
        }

        return width;
    }

    private static int GetCharDisplayWidth(char ch)
    {
        if (ch <= 0x7F)
            return 1;

        var code = (int)ch;
        if (code is >= 0x3000 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF)
            return 2;

        return 2;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarkdownRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicMarkdownRegex();
}
