namespace LocalCompanion.Services;

/// <summary>段落・文境界を優先した固定長分割（構造チャンクのオーバーフロー用も兼用）。</summary>
internal static class RagTextChunker
{
    public static IReadOnlyList<string> ChunkText(string text, int size, int overlap)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        text = text.Replace("\r\n", "\n").Trim();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var buffer = "";

        foreach (var paragraph in paragraphs)
        {
            if (buffer.Length > 0 && buffer.Length + paragraph.Length + 2 <= size)
            {
                buffer += "\n\n" + paragraph;
                continue;
            }

            if (buffer.Length > 0)
            {
                chunks.AddRange(SplitOversized(buffer, size, overlap));
                buffer = "";
            }

            if (paragraph.Length <= size)
            {
                buffer = paragraph;
                continue;
            }

            chunks.AddRange(SplitOversized(paragraph, size, overlap));
        }

        if (buffer.Length > 0)
            chunks.AddRange(SplitOversized(buffer, size, overlap));

        return chunks;
    }

    public static IEnumerable<string> SplitOversized(string text, int size, int overlap)
    {
        if (text.Length <= size)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var take = Math.Min(size, text.Length - start);
            var slice = text.Substring(start, take);

            if (start + take < text.Length)
            {
                var cut = FindSentenceBreak(slice);
                if (cut >= Math.Min(80, size / 4))
                    slice = slice[..cut].TrimEnd();
            }

            if (slice.Length > 0)
                yield return slice;

            if (start + take >= text.Length)
                break;

            start += Math.Max(1, slice.Length - overlap);
        }
    }

    private static int FindSentenceBreak(string slice)
    {
        var best = -1;
        foreach (var ch in new[] { '。', '！', '？', '.', '!', '?', '\n' })
        {
            var i = slice.LastIndexOf(ch);
            if (i > best)
                best = i;
        }

        return best >= 0 ? best + 1 : -1;
    }
}
