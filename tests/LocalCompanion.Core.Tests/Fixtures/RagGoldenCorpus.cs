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
        return conn;
    }

    public static IReadOnlyList<string> AllSources { get; } =
        [PenalSource, LaborSource, NotesSource];

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
