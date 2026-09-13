using LocalCompanion.Models;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Data;

/// <summary>
/// 概念グラフの倉庫（rag_concepts / rag_chunk_concepts / rag_concept_edges / rag_communities）。
/// 読む・書く・消すだけ。何を概念にするか・どう束ねるかは Services 側。
/// 辺は source 単位で持ち、合計はクエリ側で SUM する（source 削除で正しく減る）。
/// </summary>
public sealed class RagSqliteGraph
{
    public const string BuiltVersionKey = "graph_built_version";
    public const string BuiltAtKey = "graph_built_at";

    /// <summary>抽出・束ね方が変わったら上げる。<see cref="BuiltVersionKey"/> と違えば起動時に作り直す。</summary>
    public const string BuildVersion = "3";

    public void EnsureTables(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rag_concepts (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              term TEXT NOT NULL,
              norm_term TEXT NOT NULL UNIQUE,
              kind TEXT NOT NULL DEFAULT 'term'
            );
            CREATE TABLE IF NOT EXISTS rag_chunk_concepts (
              chunk_id INTEGER NOT NULL,
              concept_id INTEGER NOT NULL,
              count INTEGER NOT NULL DEFAULT 1,
              PRIMARY KEY (chunk_id, concept_id)
            );
            CREATE INDEX IF NOT EXISTS idx_rag_chunk_concepts_concept ON rag_chunk_concepts(concept_id);
            CREATE TABLE IF NOT EXISTS rag_concept_edges (
              a INTEGER NOT NULL,
              b INTEGER NOT NULL,
              source TEXT NOT NULL,
              kind TEXT NOT NULL,
              weight REAL NOT NULL,
              PRIMARY KEY (a, b, source, kind)
            );
            CREATE INDEX IF NOT EXISTS idx_rag_concept_edges_b ON rag_concept_edges(b);
            CREATE INDEX IF NOT EXISTS idx_rag_concept_edges_source ON rag_concept_edges(source);
            CREATE TABLE IF NOT EXISTS rag_communities (
              id INTEGER PRIMARY KEY,
              level INTEGER NOT NULL,
              parent_id INTEGER,
              label TEXT NOT NULL DEFAULT '',
              size INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS rag_concept_communities (
              concept_id INTEGER NOT NULL,
              level INTEGER NOT NULL,
              community_id INTEGER NOT NULL,
              PRIMARY KEY (concept_id, level)
            );
            CREATE INDEX IF NOT EXISTS idx_rag_concept_communities_comm ON rag_concept_communities(community_id);
            CREATE TABLE IF NOT EXISTS rag_graph_meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>source のチャンク概念と辺を消す（rag_chunks の行を消す前に呼ぶ）。</summary>
    public void DeleteSource(SqliteConnection conn, string source, SqliteTransaction? tx = null)
    {
        Exec(conn, tx, """
            DELETE FROM rag_chunk_concepts
            WHERE chunk_id IN (SELECT id FROM rag_chunks WHERE source = $s)
            """, ("$s", source));
        Exec(conn, tx, "DELETE FROM rag_concept_edges WHERE source = $s", ("$s", source));
    }

    /// <summary>source の抽出結果を差し替える。呼び出し前に <see cref="DeleteSource"/> 済みでも、孤児は掃除する。</summary>
    public void ReplaceSource(
        SqliteConnection conn,
        string source,
        IReadOnlyList<RagChunkConcepts> chunks,
        IReadOnlyList<RagConceptEdgeDraft> edges,
        SqliteTransaction? tx = null)
    {
        DeleteSource(conn, source, tx);
        Exec(conn, tx, "DELETE FROM rag_chunk_concepts WHERE chunk_id NOT IN (SELECT id FROM rag_chunks)");

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            foreach (var c in chunk.Concepts)
            {
                if (!ids.TryGetValue(c.NormTerm, out var id))
                {
                    id = UpsertConcept(conn, tx, c.Term, c.NormTerm, c.Kind);
                    ids[c.NormTerm] = id;
                }

                Exec(conn, tx, """
                    INSERT INTO rag_chunk_concepts (chunk_id, concept_id, count) VALUES ($c, $k, $n)
                    ON CONFLICT(chunk_id, concept_id) DO UPDATE SET count = excluded.count
                    """, ("$c", chunk.ChunkId), ("$k", id), ("$n", c.Count));
            }
        }

        foreach (var e in edges)
        {
            if (!ids.TryGetValue(e.NormA, out var a))
                ids[e.NormA] = a = UpsertConcept(conn, tx, e.NormA, e.NormA, RagConceptKind.Term);
            if (!ids.TryGetValue(e.NormB, out var b))
                ids[e.NormB] = b = UpsertConcept(conn, tx, e.NormB, e.NormB, RagConceptKind.Term);
            if (a == b)
                continue;
            var (lo, hi) = a < b ? (a, b) : (b, a);
            Exec(conn, tx, """
                INSERT INTO rag_concept_edges (a, b, source, kind, weight) VALUES ($a, $b, $s, $k, $w)
                ON CONFLICT(a, b, source, kind) DO UPDATE SET weight = rag_concept_edges.weight + excluded.weight
                """, ("$a", lo), ("$b", hi), ("$s", source), ("$k", e.Kind), ("$w", e.Weight));
        }

        PruneUnusedConcepts(conn, tx);
    }

    /// <summary>どのチャンクにも辺にも現れない概念を消す（source 削除後の掃除）。</summary>
    public void PruneUnusedConcepts(SqliteConnection conn, SqliteTransaction? tx = null) =>
        Exec(conn, tx, """
            DELETE FROM rag_concepts
            WHERE id NOT IN (SELECT concept_id FROM rag_chunk_concepts)
              AND id NOT IN (SELECT a FROM rag_concept_edges)
              AND id NOT IN (SELECT b FROM rag_concept_edges)
            """);

    public IReadOnlyList<(long Id, string Term, string NormTerm, string Kind)> LoadConcepts(SqliteConnection conn)
    {
        var list = new List<(long, string, string, string)>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, term, norm_term, kind FROM rag_concepts ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return list;
    }

    /// <summary>全 source を合計した辺（a &lt; b）。</summary>
    public IReadOnlyList<(long A, long B, double Weight, string Kind)> LoadAggregatedEdges(SqliteConnection conn)
    {
        var list = new List<(long, long, double, string)>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT a, b, SUM(weight), kind FROM rag_concept_edges GROUP BY a, b, kind ORDER BY a, b";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetString(3)));
        return list;
    }

    /// <summary>概念ごとの代表見出し（最頻の header_text。ラベル用）。</summary>
    public IReadOnlyDictionary<long, string> LoadTopHeaderPerConcept(SqliteConnection conn)
    {
        var map = new Dictionary<long, string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cc.concept_id, c.header_text, COUNT(*) AS n
            FROM rag_chunk_concepts cc
            INNER JOIN rag_chunks c ON c.id = cc.chunk_id
            WHERE c.header_text <> ''
            GROUP BY cc.concept_id, c.header_text
            ORDER BY cc.concept_id, n DESC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (!map.ContainsKey(id))
                map[id] = reader.GetString(1);
        }

        return map;
    }

    public void ReplaceCommunities(SqliteConnection conn, IReadOnlyList<RagCommunity> communities, SqliteTransaction? tx = null)
    {
        Exec(conn, tx, "DELETE FROM rag_concept_communities");
        Exec(conn, tx, "DELETE FROM rag_communities");
        foreach (var c in communities)
        {
            Exec(conn, tx, """
                INSERT INTO rag_communities (id, level, parent_id, label, size) VALUES ($id, $lv, $p, $l, $n)
                """, ("$id", c.Id), ("$lv", c.Level), ("$p", (object?)c.ParentId ?? DBNull.Value), ("$l", c.Label), ("$n", c.Size));
            foreach (var member in c.MemberConceptIds)
            {
                Exec(conn, tx, """
                    INSERT INTO rag_concept_communities (concept_id, level, community_id) VALUES ($c, $lv, $id)
                    ON CONFLICT(concept_id, level) DO UPDATE SET community_id = excluded.community_id
                    """, ("$c", member), ("$lv", c.Level), ("$id", c.Id));
            }
        }
    }

    /// <summary>正規化語 → 概念 id（存在するものだけ）。</summary>
    public IReadOnlyDictionary<string, long> FindConceptIds(SqliteConnection conn, IReadOnlyCollection<string> normTerms)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        if (normTerms.Count == 0)
            return map;

        var cmd = conn.CreateCommand();
        var names = BindList(cmd, normTerms, "n");
        cmd.CommandText = $"SELECT norm_term, id FROM rag_concepts WHERE norm_term IN ({names})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(0)] = reader.GetInt64(1);
        return map;
    }

    /// <summary>与えた概念の隣接概念を合計重み順に（自分自身は除く）。</summary>
    public IReadOnlyList<(long Id, string Term, double Weight)> TopNeighbors(SqliteConnection conn, IReadOnlyCollection<long> conceptIds, int top)
    {
        if (conceptIds.Count == 0 || top <= 0)
            return Array.Empty<(long, string, double)>();

        var cmd = conn.CreateCommand();
        var ids = BindList(cmd, conceptIds, "c");
        cmd.CommandText = $"""
            SELECT n.id, n.term, SUM(e.weight) AS w
            FROM (
              SELECT b AS other, weight FROM rag_concept_edges WHERE a IN ({ids})
              UNION ALL
              SELECT a AS other, weight FROM rag_concept_edges WHERE b IN ({ids})
            ) e
            INNER JOIN rag_concepts n ON n.id = e.other
            WHERE n.id NOT IN ({ids})
            GROUP BY n.id, n.term
            ORDER BY w DESC, n.id
            LIMIT $top
            """;
        cmd.Parameters.AddWithValue("$top", top);
        var list = new List<(long, string, double)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));
        return list;
    }

    /// <summary>チャンク群に現れる概念 id。</summary>
    public IReadOnlyList<long> ConceptIdsForChunks(SqliteConnection conn, IReadOnlyCollection<long> chunkIds)
    {
        if (chunkIds.Count == 0)
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        var ids = BindList(cmd, chunkIds, "k");
        cmd.CommandText = $"SELECT DISTINCT concept_id FROM rag_chunk_concepts WHERE chunk_id IN ({ids})";
        return ReadLongs(cmd);
    }

    /// <summary>概念群が属するコミュニティ id（指定 level）。</summary>
    public IReadOnlyList<long> CommunityIdsForConcepts(SqliteConnection conn, IReadOnlyCollection<long> conceptIds, int level)
    {
        if (conceptIds.Count == 0)
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        var ids = BindList(cmd, conceptIds, "c");
        cmd.CommandText = $"SELECT DISTINCT community_id FROM rag_concept_communities WHERE level = $lv AND concept_id IN ({ids})";
        cmd.Parameters.AddWithValue("$lv", level);
        return ReadLongs(cmd);
    }

    /// <summary>チャンク → 所属コミュニティ（指定 level）。1 チャンクが複数コミュニティに掛かることもある。</summary>
    public IReadOnlyList<(long ChunkId, long CommunityId)> CommunitiesForChunks(SqliteConnection conn, IReadOnlyCollection<long> chunkIds, int level)
    {
        if (chunkIds.Count == 0)
            return Array.Empty<(long, long)>();

        var cmd = conn.CreateCommand();
        var ids = BindList(cmd, chunkIds, "k");
        cmd.CommandText = $"""
            SELECT DISTINCT cc.chunk_id, k.community_id
            FROM rag_chunk_concepts cc
            INNER JOIN rag_concept_communities k ON k.concept_id = cc.concept_id AND k.level = $lv
            WHERE cc.chunk_id IN ({ids})
            ORDER BY cc.chunk_id, k.community_id
            """;
        cmd.Parameters.AddWithValue("$lv", level);
        var list = new List<(long, long)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return list;
    }

    public IReadOnlyDictionary<long, string> LoadCommunityLabels(SqliteConnection conn, IReadOnlyCollection<long> communityIds)
    {
        var map = new Dictionary<long, string>();
        if (communityIds.Count == 0)
            return map;

        var cmd = conn.CreateCommand();
        var ids = BindList(cmd, communityIds, "m");
        cmd.CommandText = $"SELECT id, label FROM rag_communities WHERE id IN ({ids})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            map[reader.GetInt64(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        return map;
    }

    /// <summary>コミュニティに属する概念を多く含むチャンクから順に（有効 source のみ）。</summary>
    public IReadOnlyList<long> RankChunksByCommunities(
        SqliteConnection conn,
        IReadOnlyCollection<long> communityIds,
        int level,
        IReadOnlyCollection<string> sources,
        int limit)
    {
        if (communityIds.Count == 0 || sources.Count == 0 || limit <= 0)
            return Array.Empty<long>();

        var cmd = conn.CreateCommand();
        var comms = BindList(cmd, communityIds, "m");
        var srcs = BindList(cmd, sources, "s");
        cmd.CommandText = $"""
            SELECT cc.chunk_id, COUNT(DISTINCT cc.concept_id) AS n, SUM(cc.count) AS total
            FROM rag_chunk_concepts cc
            INNER JOIN rag_concept_communities k ON k.concept_id = cc.concept_id AND k.level = $lv
            INNER JOIN rag_chunks c ON c.id = cc.chunk_id
            WHERE k.community_id IN ({comms}) AND c.source IN ({srcs})
            GROUP BY cc.chunk_id
            ORDER BY n DESC, total DESC, cc.chunk_id
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$lv", level);
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadLongs(cmd);
    }

    public RagGraphStats GetStats(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM rag_concepts),
              (SELECT COUNT(*) FROM (SELECT 1 FROM rag_concept_edges GROUP BY a, b)),
              (SELECT COUNT(*) FROM rag_communities WHERE level = 0)
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new RagGraphStats(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), GetMeta(conn, BuiltAtKey));
    }

    public string? GetMeta(SqliteConnection conn, string key)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM rag_graph_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar()?.ToString();
    }

    public void SetMeta(SqliteConnection conn, string key, string value, SqliteTransaction? tx = null) =>
        Exec(conn, tx, """
            INSERT INTO rag_graph_meta (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """, ("$k", key), ("$v", value));

    private static long UpsertConcept(SqliteConnection conn, SqliteTransaction? tx, string term, string norm, RagConceptKind kind)
    {
        var kindValue = KindToStorage(kind);
        // 既にあるなら、より強い種類（article > entry > heading > term）だけ上書き
        Exec(conn, tx, """
            INSERT INTO rag_concepts (term, norm_term, kind) VALUES ($t, $n, $k)
            ON CONFLICT(norm_term) DO UPDATE SET kind = excluded.kind
            WHERE (CASE excluded.kind WHEN 'article' THEN 3 WHEN 'entry' THEN 2 WHEN 'heading' THEN 1 ELSE 0 END)
                > (CASE rag_concepts.kind WHEN 'article' THEN 3 WHEN 'entry' THEN 2 WHEN 'heading' THEN 1 ELSE 0 END)
            """, ("$t", term), ("$n", norm), ("$k", kindValue));

        var select = conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = "SELECT id FROM rag_concepts WHERE norm_term = $n";
        select.Parameters.AddWithValue("$n", norm);
        return Convert.ToInt64(select.ExecuteScalar());
    }

    public static string KindToStorage(RagConceptKind kind) => kind switch
    {
        RagConceptKind.Article => "article",
        RagConceptKind.Entry => "entry",
        RagConceptKind.Heading => "heading",
        _ => "term",
    };

    /// <summary>IN 句のパラメータを束ねて "$p0, $p1, …" を返す。</summary>
    private static string BindList<T>(SqliteCommand cmd, IEnumerable<T> values, string prefix)
    {
        var names = new List<string>();
        var i = 0;
        foreach (var value in values)
        {
            var name = $"${prefix}{i++}";
            cmd.Parameters.AddWithValue(name, value!);
            names.Add(name);
        }

        return string.Join(", ", names);
    }

    private static List<long> ReadLongs(SqliteCommand cmd)
    {
        var list = new List<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetInt64(0));
        return list;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
