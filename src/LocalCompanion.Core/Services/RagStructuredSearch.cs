using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Services;

/// <summary>article_sort_key / penalty_lead 列を使う構造化検索。</summary>
internal static class RagStructuredSearch
{
    public static IReadOnlyList<RagSearchHit> Execute(
        SqliteConnection conn,
        RagQueryPlan plan,
        IReadOnlyList<string> sources,
        int topK)
    {
        if (sources.Count == 0)
            return Array.Empty<RagSearchHit>();

        return plan.Intent switch
        {
            RagQueryIntent.Boundary when plan.Boundary is not null =>
                LoadBoundary(conn, sources, plan.Boundary.Value, topK),
            RagQueryIntent.Article when plan.ArticleBindings is { Count: > 0 } =>
                LoadByArticleBindings(conn, sources, plan.ArticleBindings, topK),
            RagQueryIntent.Article when RagArticleHitFilter.RequestedKeys(plan).Count > 0 =>
                LoadByArticleSortKeys(conn, sources, RagArticleHitFilter.RequestedKeys(plan), topK),
            RagQueryIntent.Penalty when !string.IsNullOrWhiteSpace(plan.TopicKeyword) =>
                LoadByPenaltyTopic(conn, sources, plan.TopicKeyword!, topK),
            RagQueryIntent.Definition when !string.IsNullOrWhiteSpace(plan.TopicKeyword) =>
                LoadByEntryKey(conn, sources, plan.TopicKeyword!, topK, faqPreferred: false),
            RagQueryIntent.Faq when !string.IsNullOrWhiteSpace(plan.TopicKeyword) =>
                LoadByEntryKey(conn, sources, plan.TopicKeyword!, topK, faqPreferred: true),
            _ => Array.Empty<RagSearchHit>(),
        };
    }

    private static IReadOnlyList<RagSearchHit> LoadByArticleBindings(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        IReadOnlyList<RagArticleBinding> bindings,
        int topK)
    {
        var merged = new List<RagSearchHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            var scoped = sources.Where(s => RagSourceHintResolver.MatchesHint(s, binding.Hint)).ToList();
            if (scoped.Count == 0)
                scoped = sources.ToList();

            foreach (var hit in LoadByArticleSortKey(conn, scoped, binding.SortKey, topK))
            {
                if (!seen.Add(hit.ChunkId))
                    continue;
                merged.Add(hit);
            }
        }

        return merged;
    }

    private static IReadOnlyList<RagSearchHit> LoadByArticleSortKeys(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        IReadOnlyList<long> sortKeys,
        int topK)
    {
        if (sortKeys.Count == 1)
            return LoadByArticleSortKey(conn, sources, sortKeys[0], topK);

        var merged = new List<RagSearchHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in sortKeys)
        {
            foreach (var hit in LoadByArticleSortKey(conn, sources, key, topK))
            {
                if (!seen.Add(hit.ChunkId))
                    continue;
                merged.Add(hit);
            }
        }

        return merged;
    }

    private static IReadOnlyList<RagSearchHit> LoadByArticleSortKey(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        long sortKey,
        int topK)
    {
        if (sources.Count > 1)
        {
            var per = RagArticleSourceDiversifier.PerSourceTopK(topK, sources.Count);
            return RagArticleSourceDiversifier.MergePerSource(
                sources,
                src => LoadByArticleSortKey(conn, [src], sortKey, per));
        }

        var hits = QueryByArticleSortKey(conn, sources, sortKey, topK);
        if (hits.Count > 0)
            return hits;

        return LoadByArticleHeaderPrefix(conn, sources, sortKey, topK);
    }

    private static IReadOnlyList<RagSearchHit> QueryByArticleSortKey(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        long sortKey,
        int topK)
    {
        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        var limit = Math.Clamp(Math.Max(topK, 4), 1, 16);
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND article_sort_key = $key
            ORDER BY id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$key", sortKey);
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadHits(cmd);
    }

    internal static IReadOnlyList<RagSearchHit> LoadByArticleHeaderPrefix(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        long sortKey,
        int topK)
    {
        var articleNumber = (int)(sortKey / 100);
        if (articleNumber <= 0)
            return Array.Empty<RagSearchHit>();

        var prefixes = RagArticleQueryParser.BuildHeaderPrefixes(articleNumber);
        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        var matchParts = new List<string>();
        // 本文中の「第N条」言及は拾わない（見出しヘッダのみ）
        for (var i = 0; i < prefixes.Count; i++)
        {
            var headerParam = $"$hp{i}";
            matchParts.Add($"header_text LIKE {headerParam}");
            cmd.Parameters.AddWithValue(headerParam, prefixes[i] + "%");
        }

        var limit = Math.Clamp(Math.Max(topK, 4), 1, 16);
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND ({string.Join(" OR ", matchParts)})
            ORDER BY
              CASE WHEN header_text LIKE $pref0 THEN 0
                   WHEN header_text != '' THEN 1
                   ELSE 2 END,
              id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$pref0", prefixes[0] + "%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadHits(cmd);
    }

    private static IReadOnlyList<RagSearchHit> LoadByPenaltyTopic(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        string keyword,
        int topK)
    {
        var patterns = RagPenaltyTopicParser.BuildTextPatterns(keyword);
        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        var matchParts = new List<string>();
        for (var i = 0; i < patterns.Count; i++)
        {
            var headerParam = $"$hp{i}";
            var textParam = $"$tp{i}";
            var penaltyParam = $"$pp{i}";
            matchParts.Add(
                $"(header_text LIKE {headerParam} OR text LIKE {textParam} OR penalty_lead LIKE {penaltyParam})");
            cmd.Parameters.AddWithValue(headerParam, "%" + patterns[i] + "%");
            cmd.Parameters.AddWithValue(textParam, "%" + patterns[i] + "%");
            cmd.Parameters.AddWithValue(penaltyParam, "%" + patterns[i] + "%");
        }

        var limit = Math.Clamp(Math.Max(topK, 4), 1, 12);
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND ({string.Join(" OR ", matchParts)})
            ORDER BY
              CASE WHEN penalty_lead != '' AND header_text LIKE $hk THEN 0
                   WHEN penalty_lead != '' THEN 1
                   ELSE 2 END,
              id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$hk", "%" + keyword + "%");
        cmd.Parameters.AddWithValue("$limit", limit);
        var hits = ReadHits(cmd);
        if (hits.Count > 0)
            return hits;

        return LoadByPenaltyTopicFallback(conn, sources, keyword, patterns, limit);
    }

    private static IReadOnlyList<RagSearchHit> LoadByEntryKey(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        string normalizedKey,
        int topK,
        bool faqPreferred)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return Array.Empty<RagSearchHit>();

        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        var limit = Math.Clamp(Math.Max(topK, 4), 1, 12);
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND (
                entry_key = $key
                OR header_text LIKE $like
                OR (definition_lead != '' AND text LIKE $like)
              )
            ORDER BY
              CASE WHEN entry_key = $key AND chunk_kind = 'faq' AND definition_lead != '' THEN 0
                   WHEN entry_key = $key AND chunk_kind IN ('definition', 'glossary') AND definition_lead != '' THEN 1
                   WHEN entry_key = $key AND definition_lead != '' THEN 2
                   WHEN entry_key = $key THEN 3
                   WHEN chunk_kind = 'faq' THEN 4
                   WHEN chunk_kind IN ('definition', 'glossary') THEN 5
                   ELSE 6 END,
              id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$key", normalizedKey);
        cmd.Parameters.AddWithValue("$like", "%" + normalizedKey + "%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadHits(cmd);
    }

    private static IReadOnlyList<RagSearchHit> LoadByPenaltyTopicFallback(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        string keyword,
        IReadOnlyList<string> patterns,
        int limit)
    {
        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, sources, "src");
        var matchParts = new List<string>();
        for (var i = 0; i < patterns.Count; i++)
        {
            var headerParam = $"$hp{i}";
            var textParam = $"$tp{i}";
            matchParts.Add($"(header_text LIKE {headerParam} OR text LIKE {textParam})");
            cmd.Parameters.AddWithValue(headerParam, "%" + patterns[i] + "%");
            cmd.Parameters.AddWithValue(textParam, "%" + patterns[i] + "%");
        }

        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND ({string.Join(" OR ", matchParts)})
            ORDER BY CASE WHEN header_text LIKE $hk THEN 0 ELSE 1 END, id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$hk", "%" + keyword + "%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadHits(cmd);
    }

    private static IReadOnlyList<RagSearchHit> LoadBoundary(
        SqliteConnection conn,
        IReadOnlyList<string> sources,
        RagArticleBoundaryIntent boundary,
        int topK)
    {
        // 複数法令を横断して MIN/MAX すると別法の条が混ざるので、構造化条文がある資料を1つに絞る
        var boundarySources = ResolveSingleBoundarySource(conn, sources);
        var cmd = conn.CreateCommand();
        var inClause = RagSqlBuilder.InClause(cmd, boundarySources, "src");
        var agg = boundary == RagArticleBoundaryIntent.Last ? "MAX" : "MIN";
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
            FROM rag_chunks
            WHERE source IN ({inClause})
              AND article_sort_key = (
                SELECT {agg}(article_sort_key)
                FROM rag_chunks
                WHERE source IN ({inClause}) AND article_sort_key > 0
              )
            ORDER BY id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(Math.Max(topK, 4), 1, 12));
        var hits = ReadHits(cmd);
        if (hits.Count == 0)
            return Array.Empty<RagSearchHit>();

        var targetKey = hits[0].ArticleSortKey;
        if (targetKey <= 0 && !string.IsNullOrWhiteSpace(hits[0].HeaderText)
            && RagArticleQueryParser.TryParseArticleSortKey(hits[0].HeaderText, out var parsed))
        {
            targetKey = parsed;
        }

        var label = targetKey > 0
            ? RagArticleQueryParser.FormatArticleLabel(targetKey)
            : hits[0].HeaderText;
        var sourceName = Path.GetFileName(hits[0].Source);
        var metaText = RagBoundaryMetaFormatter.Format(boundary, sourceName, label);
        var metaHit = new RagSearchHit(
            metaText,
            hits[0].Source,
            label,
            0,
            "__boundary_meta__",
            "",
            "",
            "",
            "");

        return [metaHit, .. hits];
    }

    /// <summary>境界条クエリは資料を1つに限定（複数法令の条番号混線防止）。</summary>
    private static IReadOnlyList<string> ResolveSingleBoundarySource(
        SqliteConnection conn,
        IReadOnlyList<string> sources)
    {
        if (sources.Count <= 1)
            return sources;

        foreach (var source in sources)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM rag_chunks
                WHERE source = $s AND article_sort_key > 0
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$s", source);
            if (cmd.ExecuteScalar() is not null)
                return [source];
        }

        return [sources[0]];
    }

    private static IReadOnlyList<RagSearchHit> ReadHits(SqliteCommand cmd)
    {
        var hits = new List<RagSearchHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var text = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var penaltyLead = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var definitionLead = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "";
            hits.Add(new RagSearchHit(
                text,
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                penaltyLead,
                penaltyLead,
                definitionLead));
        }

        return hits;
    }
}

internal static class RagSqlBuilder
{
    public static string InClause(SqliteCommand cmd, IReadOnlyList<string> values, string prefix)
    {
        var parts = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var name = $"${prefix}{i}";
            parts.Add(name);
            cmd.Parameters.AddWithValue(name, values[i]);
        }

        return string.Join(", ", parts);
    }
}
