using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Data;

public sealed class RagDatabase
{
    private readonly string _dbPath;
    private readonly RagSqliteVec _vec = new();
    private readonly RagSqliteFts _fts = new();

    public RagDatabase(IOptions<LlamaOptions> opt)
    {
        var dir = AppPaths.ResolveUserDataDirectory(opt.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "rag.db");
        DataDirectory = dir;
        Initialize();
    }

    public string DataDirectory { get; }

    public RagSqliteVec Vector => _vec;

    public RagSqliteFts Fts => _fts;

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Default Timeout=30");
        conn.Open();
        ApplyConnectionPragmas(conn);
        return conn;
    }

    public void CheckpointWal()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
        cmd.ExecuteNonQuery();
    }

    private static void ApplyConnectionPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            PRAGMA mmap_size=268435456;
            """;
        cmd.ExecuteNonQuery();
    }

    public void PrepareVectors(SqliteConnection conn) => _vec.TryPrepare(conn);

    public void PrepareFts(SqliteConnection conn) => _fts.TryPrepare(conn);

    private void Initialize()
    {
        using var conn = Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rag_chunks (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              source TEXT NOT NULL,
              text TEXT NOT NULL,
              embedding TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chat_messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              role TEXT NOT NULL,
              content TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS character_profile (
              id INTEGER PRIMARY KEY CHECK (id = 1),
              name TEXT NOT NULL,
              persona TEXT NOT NULL,
              speaking_style TEXT NOT NULL,
              temperature REAL NOT NULL,
              top_p REAL NOT NULL,
              context_length INTEGER NOT NULL,
              max_output_tokens INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        var seed = conn.CreateCommand();
        seed.CommandText = """
            INSERT OR IGNORE INTO character_profile (
              id, name, persona, speaking_style, temperature, top_p, context_length, max_output_tokens
            ) VALUES (
              1, 'アシスタント', '', '', 0.8, 0.95, 8192, 4096
            );
            """;
        seed.ExecuteNonQuery();

        var cap = conn.CreateCommand();
        cap.CommandText = "UPDATE character_profile SET context_length = 16384 WHERE context_length > 24576";
        cap.ExecuteNonQuery();

        EnsureChatMessagesPresetKey(conn);
        EnsureChatMessagesSessionId(conn);
        EnsureConversationSessions(conn);
        MigrateLegacyChatSessions(conn);
        EnsureCharacterProfileTopK(conn);
        EnsureRagChunkMetadata(conn);
        EnsureRagSourcePrefs(conn);
        EnsureRagFtsBackfill(conn);
        EnsureRagStructuredBackfill(conn);
        EnsureRagUniversalBackfill(conn);
    }

    private void EnsureRagStructuredBackfill(SqliteConnection conn)
    {
        var check = conn.CreateCommand();
        check.CommandText = "SELECT value FROM app_metadata WHERE key = 'rag_structured_backfilled' LIMIT 1";
        if (check.ExecuteScalar()?.ToString() == "1")
            return;

        var select = conn.CreateCommand();
        select.CommandText = """
            SELECT id, header_text, text, parent_text
            FROM rag_chunks
            WHERE article_sort_key = 0 OR penalty_lead = ''
            """;
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var header = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var text = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var parent = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var (main, sub, sortKey) = LegalFieldExtractor.ParseArticle(header);
                var penalty = LegalFieldExtractor.ExtractPenaltyLead(header, text, parent);
                var kind = LegalFieldExtractor.ResolveChunkKind(header, parent);

                var update = conn.CreateCommand();
                update.CommandText = """
                    UPDATE rag_chunks
                    SET article_main = $m, article_sub = $s, article_sort_key = $k,
                        penalty_lead = $p, chunk_kind = $c
                    WHERE id = $id
                    """;
                update.Parameters.AddWithValue("$m", main);
                update.Parameters.AddWithValue("$s", sub);
                update.Parameters.AddWithValue("$k", sortKey);
                update.Parameters.AddWithValue("$p", penalty);
                update.Parameters.AddWithValue("$c", kind);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
        }

        var flag = conn.CreateCommand();
        flag.CommandText = """
            INSERT INTO app_metadata (key, value) VALUES ('rag_structured_backfilled', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        flag.ExecuteNonQuery();
    }

    private void EnsureRagUniversalBackfill(SqliteConnection conn)
    {
        var check = conn.CreateCommand();
        check.CommandText = "SELECT value FROM app_metadata WHERE key = 'rag_universal_backfilled' LIMIT 1";
        if (check.ExecuteScalar()?.ToString() == "1")
            return;

        var select = conn.CreateCommand();
        select.CommandText = """
            SELECT id, source, header_text, text, parent_text, header_level,
                   chapter, section, subsection, article_sort_key, chunk_kind
            FROM rag_chunks
            WHERE entry_key = '' OR entry_key IS NULL
            """;
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var source = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var header = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var text = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var parent = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var headerLevel = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                var chapter = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var section = reader.IsDBNull(7) ? "" : reader.GetString(7);
                var subsection = reader.IsDBNull(8) ? "" : reader.GetString(8);
                var articleSortKey = reader.IsDBNull(9) ? 0L : reader.GetInt64(9);
                var chunkKind = reader.IsDBNull(10) ? "" : reader.GetString(10);

                var docKind = RagDocumentProfileDetector.Detect(source, header + "\n" + text);
                var generic = RagGenericFieldExtractor.Enrich(
                    header, text, parent, headerLevel, chapter, section, subsection, docKind);

                var entryKey = generic.EntryKey;
                var definitionLead = generic.DefinitionLead;
                var resolvedKind = chunkKind;
                if (articleSortKey == 0 && generic.ChunkKind is "definition" or "glossary" or "faq")
                    resolvedKind = generic.ChunkKind;

                var update = conn.CreateCommand();
                update.CommandText = """
                    UPDATE rag_chunks
                    SET entry_key = $ek, definition_lead = $dl, section_path = $sp,
                        doc_kind = $dk, chunk_kind = $ck
                    WHERE id = $id
                    """;
                update.Parameters.AddWithValue("$ek", entryKey);
                update.Parameters.AddWithValue("$dl", definitionLead);
                update.Parameters.AddWithValue("$sp", generic.SectionPath);
                update.Parameters.AddWithValue("$dk", RagDocumentProfileDetector.ToStorageValue(docKind));
                update.Parameters.AddWithValue("$ck", string.IsNullOrWhiteSpace(resolvedKind) ? "section" : resolvedKind);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
        }

        var flag = conn.CreateCommand();
        flag.CommandText = """
            INSERT INTO app_metadata (key, value) VALUES ('rag_universal_backfilled', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        flag.ExecuteNonQuery();
    }

    private void EnsureRagFtsBackfill(SqliteConnection conn)
    {
        PrepareFts(conn);
        if (!_fts.IsAvailable)
            return;

        var check = conn.CreateCommand();
        check.CommandText = "SELECT value FROM app_metadata WHERE key = 'rag_fts_backfilled' LIMIT 1";
        if (check.ExecuteScalar()?.ToString() == "1")
            return;

        _fts.Backfill(conn);

        var flag = conn.CreateCommand();
        flag.CommandText = """
            INSERT INTO app_metadata (key, value) VALUES ('rag_fts_backfilled', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        flag.ExecuteNonQuery();
    }

    private static void EnsureRagSourcePrefs(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rag_source_prefs (
              source TEXT PRIMARY KEY,
              enabled INTEGER NOT NULL DEFAULT 1
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureRagChunkMetadata(SqliteConnection conn)
    {
        EnsureRagColumn(conn, "chunk_id", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "header_text", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "header_level", "INTEGER NOT NULL DEFAULT 0");
        EnsureRagColumn(conn, "page", "INTEGER NOT NULL DEFAULT 0");
        EnsureRagColumn(conn, "chapter", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "section", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "subsection", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "parent_text", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "article_main", "INTEGER NOT NULL DEFAULT 0");
        EnsureRagColumn(conn, "article_sub", "INTEGER NOT NULL DEFAULT 0");
        EnsureRagColumn(conn, "article_sort_key", "INTEGER NOT NULL DEFAULT 0");
        EnsureRagColumn(conn, "penalty_lead", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "chunk_kind", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "entry_key", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "definition_lead", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "section_path", "TEXT NOT NULL DEFAULT ''");
        EnsureRagColumn(conn, "doc_kind", "TEXT NOT NULL DEFAULT 'general'");
        EnsureRagIndex(conn, "idx_rag_chunks_article", "article_sort_key", "source");
        EnsureRagIndex(conn, "idx_rag_chunks_entry_key", "entry_key", "source");
    }

    private static void EnsureRagIndex(SqliteConnection conn, string name, string column, string sourceColumn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {name} ON rag_chunks({sourceColumn}, {column})";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureRagColumn(SqliteConnection conn, string column, string definition)
    {
        if (ColumnExists(conn, "rag_chunks", column))
            return;

        var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE rag_chunks ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void EnsureCharacterProfileTopK(SqliteConnection conn)
    {
        if (ColumnExists(conn, "character_profile", "top_k"))
            return;

        var alter = conn.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE character_profile ADD COLUMN top_k INTEGER NOT NULL DEFAULT {CharacterDefaults.AppTopK}";
        alter.ExecuteNonQuery();
    }

    private static void EnsureChatMessagesPresetKey(SqliteConnection conn)
    {
        if (ColumnExists(conn, "chat_messages", "preset_key"))
            return;

        var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE chat_messages ADD COLUMN preset_key TEXT NOT NULL DEFAULT ''";
        alter.ExecuteNonQuery();
    }

    private static void EnsureChatMessagesSessionId(SqliteConnection conn)
    {
        if (ColumnExists(conn, "chat_messages", "session_id"))
            return;

        var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE chat_messages ADD COLUMN session_id TEXT NOT NULL DEFAULT ''";
        alter.ExecuteNonQuery();
    }

    private static void EnsureConversationSessions(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS conversation_sessions (
              id TEXT PRIMARY KEY,
              preset_key TEXT NOT NULL,
              title TEXT NOT NULL DEFAULT '',
              summary TEXT NOT NULL DEFAULT '',
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              closed_at TEXT
            );
            CREATE TABLE IF NOT EXISTS app_metadata (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateLegacyChatSessions(SqliteConnection conn)
    {
        if (!ColumnExists(conn, "chat_messages", "session_id"))
            return;

        var check = conn.CreateCommand();
        check.CommandText = "SELECT value FROM app_metadata WHERE key = 'legacy_sessions_migrated' LIMIT 1";
        if (check.ExecuteScalar()?.ToString() == "1")
            return;

        var keys = conn.CreateCommand();
        keys.CommandText = """
            SELECT DISTINCT preset_key FROM chat_messages
            WHERE preset_key != '' AND (session_id IS NULL OR session_id = '')
            """;
        var presetKeys = new List<string>();
        using (var r = keys.ExecuteReader())
        {
            while (r.Read())
                presetKeys.Add(r.GetString(0));
        }

        foreach (var presetKey in presetKeys)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow.ToString("O");
            var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO conversation_sessions (id, preset_key, title, summary, created_at, updated_at, closed_at)
                VALUES ($id, $k, '', '', $c, $u, $u)
                """;
            insert.Parameters.AddWithValue("$id", sessionId);
            insert.Parameters.AddWithValue("$k", presetKey);
            insert.Parameters.AddWithValue("$c", now);
            insert.Parameters.AddWithValue("$u", now);
            insert.ExecuteNonQuery();

            var update = conn.CreateCommand();
            update.CommandText = """
                UPDATE chat_messages SET session_id = $id
                WHERE preset_key = $k AND (session_id IS NULL OR session_id = '')
                """;
            update.Parameters.AddWithValue("$id", sessionId);
            update.Parameters.AddWithValue("$k", presetKey);
            update.ExecuteNonQuery();
        }

        var flag = conn.CreateCommand();
        flag.CommandText = """
            INSERT INTO app_metadata (key, value) VALUES ('legacy_sessions_migrated', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        flag.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
