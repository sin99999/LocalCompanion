using System.Text;

namespace LocalCompanion.Services;

/// <summary>Web 調査で得た URL を返答末尾に確定付与する。</summary>
internal static class ChatWebSourceCitation
{
    public static string AppendIfMissing(string reply, IReadOnlyList<string>? sourceUrls, bool japanese)
    {
        if (sourceUrls is null || sourceUrls.Count == 0)
            return reply;

        var ordered = DedupPreserveOrder(sourceUrls);
        if (ordered.Count == 0)
            return reply;

        var alreadyInReply = ChatMessageUrlExtractor.Extract(reply, maxCount: 64)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ordered.All(alreadyInReply.Contains))
            return reply;

        var header = japanese ? "参考:" : "Sources:";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(reply))
        {
            sb.Append(reply.TrimEnd());
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine(header);
        foreach (var url in ordered)
            sb.AppendLine(url);

        return sb.ToString().TrimEnd();
    }

    public static IReadOnlyList<string> Merge(IReadOnlyList<string>? first, IReadOnlyList<string>? second)
    {
        if ((first is null || first.Count == 0) && (second is null || second.Count == 0))
            return Array.Empty<string>();
        if (first is null || first.Count == 0)
            return DedupPreserveOrder(second!);
        if (second is null || second.Count == 0)
            return DedupPreserveOrder(first);
        return DedupPreserveOrder(first.Concat(second));
    }

    private static List<string> DedupPreserveOrder(IEnumerable<string> urls)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in urls)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("http" or "https"))
                continue;
            if (!seen.Add(uri.AbsoluteUri))
                continue;
            list.Add(uri.AbsoluteUri);
        }

        return list;
    }
}
