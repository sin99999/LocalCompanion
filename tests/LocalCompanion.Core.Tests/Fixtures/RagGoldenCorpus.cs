using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Core.Tests.Fixtures;

/// <summary>黄金セット用の小さなローカルコーパス（llama 不要）。</summary>
internal static class RagGoldenCorpus
{
    public const string PenalSource = "刑法.md";
    public const string LaborSource = "労働基準法.md";
    public const string NotesSource = "雑談メモ.md";

    /// <summary>条文どうしが参照し合う抜粋（多段質問の評価用）。名前に「刑法」を含めず、境界条テストの絞り込みに混ざらないようにする。</summary>
    public const string TheftSource = "法令抜粋_窃盗.md";

    /// <summary>法令ではない一般資料（合図語なしの質問で RAG が動くかの評価用）。</summary>
    public const string ReadmeSource = "LocalCompanion-README.md";

    public static SqliteConnection OpenFilled()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        CreateSchema(conn);
        var fts = new RagSqliteFts();
        fts.TryPrepare(conn);

        InsertChunked(conn, fts, PenalSource, PenalCodeTestFixtures.BuildMarkdown(articleCount: 20));
        InsertChunked(conn, fts, LaborSource, BuildLaborMarkdown());
        InsertChunked(conn, fts, NotesSource, BuildNotesMarkdown());
        InsertChunked(conn, fts, TheftSource, BuildTheftMarkdown());
        InsertChunked(conn, fts, ReadmeSource, BuildReadmeMarkdown());
        return conn;
    }

    public static IReadOnlyList<string> AllSources { get; } =
        [PenalSource, LaborSource, NotesSource, TheftSource, ReadmeSource];

    /// <summary>概念グラフを LLM なしで作る（グラフレーン・俯瞰検索の評価用）。</summary>
    public static RagSqliteGraph BuildGraph(SqliteConnection conn)
    {
        var graph = new RagSqliteGraph();
        graph.EnsureTables(conn);
        new RagGraphIndexer(graph, new HeuristicWordSegmenter()).RebuildAll(conn);
        return graph;
    }

    /// <summary>llama 無しの語彙検索（Probe と同じ FTS だけの経路）。RagService.LoadHitsByIds の最小版。</summary>
    public static IReadOnlyList<RagSearchHit> SearchFts(SqliteConnection conn, string query, int topK = 8)
    {
        var fts = new RagSqliteFts();
        fts.TryPrepare(conn);
        var match = RagSqliteFts.BuildSegmentedMatchQuery(query);
        return LoadHits(conn, fts.Search(conn, match, topK, AllSources));
    }

    /// <summary>FTS ＋ グラフ第 3 レーン（隣接語で FTS を広げ、同コミュニティを RRF に加点）。ベクトルは無し。</summary>
    public static IReadOnlyList<RagSearchHit> SearchFtsWithGraphLane(SqliteConnection conn, RagSqliteGraph graph, string query, int topK = 5)
    {
        var fts = new RagSqliteFts();
        fts.TryPrepare(conn);
        var lane = new RagGraphLane(graph, new HeuristicWordSegmenter());
        var match = RagGraphLane.AppendToMatchQuery(RagSqliteFts.BuildSegmentedMatchQuery(query), lane.ExpandQueryTerms(conn, query));
        var ftsIds = fts.Search(conn, match, 16, AllSources);
        var graphIds = lane.SameCommunityChunkIds(conn, ftsIds, AllSources, 16);
        var fused = RagHybridSearch.FuseRrf(ftsIds, Array.Empty<long>(), graphIds, topK, 60, 1.0, 0, 0.2);
        return LoadHits(conn, fused);
    }

    private static IReadOnlyList<RagSearchHit> LoadHits(SqliteConnection conn, IReadOnlyList<long> ids)
    {
        var hits = new List<RagSearchHit>(ids.Count);
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead, section_path
                FROM rag_chunks WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                continue;
            hits.Add(new RagSearchHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                "",
                reader.IsDBNull(7) ? "" : reader.GetString(7),
                reader.IsDBNull(8) ? "" : reader.GetString(8)));
        }

        return hits;
    }

    /// <summary>第243条は「第235条から第236条まで」とだけ書き、窃盗の語を含めない（参照エッジで辿れるかを見る）。</summary>
    public static string BuildTheftMarkdown() =>
        """
        # 法令抜粋（窃盗及び強盗の罪）

        #### 第235条（窃盗）

        他人の財物を窃取した者は、窃盗の罪とし、十年以下の懲役又は五十万円以下の罰金に処する。

        #### 第236条（強盗）

        暴行又は脅迫を用いて他人の財物を強取した者は、強盗の罪とし、五年以上の有期懲役に処する。

        #### 第243条（未遂罪）

        第235条から第236条までの罪の未遂は、罰する。

        #### 第244条（親族間の犯罪に関する特例）

        配偶者、直系血族又は同居の親族との間で第235条の罪を犯した者は、その刑を免除する。
        """;

    public static string BuildReadmeMarkdown() =>
        """
        # LocalCompanion

        ## 必要なもの

        .NET 10 Desktop Runtime（x64）と Microsoft Edge WebView2 Runtime が必要です。WebView2 は ZIP に同梱していません。

        ## ビルド

        リポジトリのルートで `dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64` を実行します。Platform に x64 を渡してください。

        ## データの保存場所

        Debug 起動では LocalAppData の LocalCompanionLlama フォルダに、配布版では exe の横の data フォルダに保存します。

        ## 起動しないとき

        画面が表示されない場合は WebView2 Runtime が入っているか確認してください。初回起動ではモデルのダウンロードに時間がかかります。
        """;

    private static string BuildLaborMarkdown() =>
        """
        # 労働基準法

        #### 第11条（定義）

        この法律で賃金とは、賃金、給料、手当、賞与その他名称の如何を問わず、労働の対償として使用者が労働者に支払うすべてのものをいう。

        #### 第32条（労働時間）

        使用者は、労働者に、休憩時間を除き一週間について四十時間を超えて、労働させてはならない。
        """;

    private static string BuildNotesMarkdown() =>
        """
        # 雑談メモ

        ## AIの話

        AIさんは女の子ですか、という雑談メモ。残業や法律の話ではない。
        """;

    private static void CreateSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE rag_chunks (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              source TEXT NOT NULL,
              text TEXT NOT NULL,
              embedding TEXT NOT NULL DEFAULT '[]',
              created_at TEXT NOT NULL,
              chunk_id TEXT,
              header_text TEXT DEFAULT '',
              header_level INTEGER NOT NULL DEFAULT 0,
              page INTEGER NOT NULL DEFAULT 0,
              chapter TEXT DEFAULT '',
              section TEXT DEFAULT '',
              subsection TEXT DEFAULT '',
              parent_text TEXT DEFAULT '',
              article_main INTEGER NOT NULL DEFAULT 0,
              article_sub INTEGER NOT NULL DEFAULT 0,
              article_sort_key INTEGER NOT NULL DEFAULT 0,
              penalty_lead TEXT DEFAULT '',
              chunk_kind TEXT DEFAULT '',
              entry_key TEXT DEFAULT '',
              definition_lead TEXT DEFAULT '',
              section_path TEXT DEFAULT '',
              doc_kind TEXT DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertChunked(
        SqliteConnection conn,
        RagSqliteFts fts,
        string source,
        string markdown)
    {
        var text = RagDocumentNormalizer.Normalize(markdown);
        var docKind = RagDocumentProfileDetector.Detect(source, text);
        var drafts = RagStructuralChunker.CreateChunks(text, source, size: 900, overlap: 128, docKind);
        foreach (var draft in drafts)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO rag_chunks (
                  source, text, embedding, created_at,
                  chunk_id, header_text, header_level, page, chapter, section, subsection, parent_text,
                  article_main, article_sub, article_sort_key, penalty_lead, chunk_kind,
                  entry_key, definition_lead, section_path, doc_kind
                )
                VALUES (
                  $s, $t, '[]', $at,
                  $cid, $ht, $hl, $pg, $ch, $sec, $sub, $parent,
                  $am, $as, $ask, $pl, $ck,
                  $ek, $dl, $sp, $dk
                );
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$s", source);
            cmd.Parameters.AddWithValue("$t", draft.Text);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$cid", draft.ChunkId);
            cmd.Parameters.AddWithValue("$ht", draft.HeaderText);
            cmd.Parameters.AddWithValue("$hl", draft.HeaderLevel);
            cmd.Parameters.AddWithValue("$pg", draft.Page);
            cmd.Parameters.AddWithValue("$ch", draft.Chapter);
            cmd.Parameters.AddWithValue("$sec", draft.Section);
            cmd.Parameters.AddWithValue("$sub", draft.Subsection);
            cmd.Parameters.AddWithValue("$parent", draft.ParentText);
            cmd.Parameters.AddWithValue("$am", draft.ArticleMain);
            cmd.Parameters.AddWithValue("$as", draft.ArticleSub);
            cmd.Parameters.AddWithValue("$ask", draft.ArticleSortKey);
            cmd.Parameters.AddWithValue("$pl", draft.PenaltyLead);
            cmd.Parameters.AddWithValue("$ck", draft.ChunkKind);
            cmd.Parameters.AddWithValue("$ek", draft.EntryKey);
            cmd.Parameters.AddWithValue("$dl", draft.DefinitionLead);
            cmd.Parameters.AddWithValue("$sp", draft.SectionPath);
            cmd.Parameters.AddWithValue("$dk", draft.DocKind);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            fts.IndexChunk(conn, id, draft.Text, draft.HeaderText, draft.Chapter, draft.Section);
        }
    }
}
