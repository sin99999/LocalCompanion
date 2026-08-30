using Microsoft.Data.Sqlite;

namespace LocalCompanion.Services;

/// <summary>登録資料の「列・棚・段」（source と見出しパス）を一覧する。</summary>
internal sealed record RagShelf(string Source, string SectionPath, string HeaderText, int ChunkCount);

internal static class RagShelfCatalog
{
    public static IReadOnlyList<RagShelf> Load(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        int limit = 400)
    {
        if (sources.Count == 0)
            return Array.Empty<RagShelf>();

        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        cmd.CommandText = $"""
            SELECT source, section_path, header_text, COUNT(*)
            FROM rag_chunks
            WHERE source IN ({inClause})
            GROUP BY source, section_path, header_text
            ORDER BY COUNT(*) DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 800));

        var list = new List<RagShelf>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RagShelf(
                reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetInt32(3)));
        }

        return list;
    }

    public static IReadOnlyList<RagShelf> Rank(string query, IReadOnlyList<RagShelf> shelves, int take = 8)
    {
        if (shelves.Count == 0)
            return Array.Empty<RagShelf>();

        return shelves
            .Select(s => (Shelf: s, Score: Score(query, s)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Shelf.ChunkCount)
            .Take(Math.Clamp(take, 1, 16))
            .Select(x => x.Shelf)
            .ToList();
    }

    public static int Score(string query, RagShelf shelf)
    {
        var hay = string.Concat(
            Path.GetFileName(shelf.Source), "\n",
            shelf.SectionPath, "\n",
            shelf.HeaderText);
        var needles = RagConversationGate.ExtractNeedles(query);
        if (needles.Count == 0)
            return 0;

        var score = 0;
        foreach (var needle in needles)
        {
            if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase))
                score += needle.Length >= 4 ? 3 : 1;
        }

        return score;
    }

    public static string? SectionNeedle(string query, IReadOnlyList<RagShelf> ranked)
    {
        if (ranked.Count == 0)
            return null;

        var needles = RagConversationGate.ExtractNeedles(query)
            .OrderByDescending(n => n.Length)
            .ToList();
        if (needles.Count == 0)
            return null;

        var top = ranked[0];
        var hay = string.Concat(top.SectionPath, "\n", top.HeaderText);
        foreach (var needle in needles)
        {
            if (needle.Length < 2)
                continue;
            if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return needle;
        }

        return null;
    }

    public static IReadOnlyList<string> DistinctSources(IReadOnlyList<RagShelf> ranked, int take = 4)
    {
        var list = new List<string>();
        foreach (var shelf in ranked)
        {
            if (list.Contains(shelf.Source, StringComparer.OrdinalIgnoreCase))
                continue;
            list.Add(shelf.Source);
            if (list.Count >= take)
                break;
        }

        return list;
    }
}
