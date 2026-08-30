using LocalCompanion;
using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Core.Tests;

public sealed class MemoryServicePersonaScopeTests
{
    [Fact]
    public void SupportsLongTermMemory_SkipsDefaultAi()
    {
        Assert.False(MemoryService.SupportsLongTermMemory(CharacterPresetService.DefaultAiPresetKey));
        Assert.False(MemoryService.SupportsLongTermMemory(""));
        Assert.False(MemoryService.SupportsLongTermMemory(null));
        Assert.True(MemoryService.SupportsLongTermMemory("example.json"));
    }

    [Fact]
    public async Task AddAndList_AreScopedPerCharacter_AndDefaultAiGetsNothing()
    {
        await using var fx = await MemoryTestFixture.CreateAsync();
        var memory = fx.Memory;

        Assert.NotNull(await memory.AddAsync("好きな色は青", "alpha.json"));
        Assert.NotNull(await memory.AddAsync("好きな色は赤", "beta.json"));
        Assert.NotNull(await memory.AddAsync("共通の好み", "alpha.json"));
        Assert.NotNull(await memory.AddAsync("共通の好み", "beta.json"));
        Assert.Null(await memory.AddAsync("プレーン用", CharacterPresetService.DefaultAiPresetKey));

        var alpha = memory.List("alpha.json");
        var beta = memory.List("beta.json");
        Assert.Equal(2, alpha.Count);
        Assert.Equal(2, beta.Count);
        Assert.Contains(alpha, m => m.Content == "好きな色は青");
        Assert.DoesNotContain(alpha, m => m.Content == "好きな色は赤");
        Assert.All(alpha, m => Assert.Equal("alpha.json", m.PresetKey));
        Assert.Empty(memory.List(CharacterPresetService.DefaultAiPresetKey));

        var defaultHits = await memory.GetRelevantForPromptAsync("やあ", CharacterPresetService.DefaultAiPresetKey);
        Assert.Empty(defaultHits);

        // あいさつだけでは関連検索ヒットなし → 乱択回想もしない
        var alphaCasual = await memory.GetRelevantForPromptAsync("やあ", "alpha.json");
        Assert.Empty(alphaCasual);

        var alphaRelated = await memory.GetRelevantForPromptAsync("好きな色は青", "alpha.json");
        Assert.NotEmpty(alphaRelated);
        Assert.True(alphaRelated.Count <= 1);
        Assert.All(alphaRelated, m => Assert.Equal("alpha.json", m.PresetKey));
        Assert.Contains(alphaRelated, m => m.Content.Contains("青", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractFromSession_DefaultAi_ReturnsZeroWithoutSaving()
    {
        await using var fx = await MemoryTestFixture.CreateAsync();
        InsertSession(fx.Db, "sess-default", CharacterPresetService.DefaultAiPresetKey);

        var saved = await fx.Memory.ExtractFromSessionAsync(
            "sess-default",
            [("user", "僕は山田です"), ("assistant", "よろしくお願いします")],
            CancellationToken.None);

        Assert.Equal(0, saved);
        Assert.Empty(fx.Memory.List("alpha.json"));
        Assert.Empty(ListAllMemories(fx.Db));
    }

    [Fact]
    public void EnsureUserMemories_MigratesLegacyTableWithoutPresetKey()
    {
        var relative = Path.Combine("obj", "lc-mem-legacy-" + Guid.NewGuid().ToString("N"));
        var opt = Options.Create(new LlamaOptions { DataDirectory = relative });
        var dir = AppPaths.ResolveUserDataDirectory(relative);
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "rag.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE user_memories (
                      id INTEGER PRIMARY KEY AUTOINCREMENT,
                      content TEXT NOT NULL,
                      memory_path TEXT NOT NULL DEFAULT '',
                      source_session_id TEXT NOT NULL DEFAULT '',
                      created_at TEXT NOT NULL,
                      updated_at TEXT NOT NULL
                    );
                    INSERT INTO user_memories (content, memory_path, source_session_id, created_at, updated_at)
                    VALUES ('旧記憶', 'session', '', 't', 't');
                    """;
                cmd.ExecuteNonQuery();
            }

            // 旧スキーマのまま Initialize しても落ちないこと
            var db = new RagDatabase(opt);
            var rows = ListAllMemories(db);
            Assert.Single(rows);
            Assert.Equal("旧記憶", rows[0].Content);
            Assert.Equal("", rows[0].PresetKey);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Backfill_AssignsPresetFromSession_AndDropsDefaultAiRows()
    {
        var relative = Path.Combine("obj", "lc-mem-bf-" + Guid.NewGuid().ToString("N"));
        var opt = Options.Create(new LlamaOptions { DataDirectory = relative });
        var db = new RagDatabase(opt);
        var dir = db.DataDirectory;
        try
        {
            InsertSession(db, "s-char", "hero.json");
            InsertSession(db, "s-default", CharacterPresetService.DefaultAiPresetKey);
            InsertLegacyMemory(db, "キャラ用の事実", "s-char");
            InsertLegacyMemory(db, "プレーン由来", "s-default");

            // マーカーを消して再 backfill
            using (var conn = db.Open())
            {
                conn.Open();
                var del = conn.CreateCommand();
                del.CommandText = "DELETE FROM app_metadata WHERE key = 'user_memories_preset_backfill_v1'";
                del.ExecuteNonQuery();
            }

            // 新しい RagDatabase 初期化で backfill を再実行
            _ = new RagDatabase(opt);

            var rows = ListAllMemories(db);
            Assert.Single(rows);
            Assert.Equal("キャラ用の事実", rows[0].Content);
            Assert.Equal("hero.json", rows[0].PresetKey);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static void InsertSession(RagDatabase db, string id, string presetKey)
    {
        using var conn = db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_sessions (id, preset_key, title, summary, created_at, updated_at)
            VALUES ($id, $k, '', '', $t, $t)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$k", presetKey);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void InsertLegacyMemory(RagDatabase db, string content, string sessionId)
    {
        using var conn = db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_memories (content, memory_path, source_session_id, preset_key, created_at, updated_at)
            VALUES ($c, 'session', $s, '', $t, $t)
            """;
        cmd.Parameters.AddWithValue("$c", content);
        cmd.Parameters.AddWithValue("$s", sessionId);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static List<UserMemoryRecord> ListAllMemories(RagDatabase db)
    {
        using var conn = db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, content, memory_path, source_session_id, created_at, preset_key
            FROM user_memories
            ORDER BY id
            """;
        var list = new List<UserMemoryRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UserMemoryRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5)));
        }

        return list;
    }

    private sealed class MemoryTestFixture : IAsyncDisposable
    {
        private MemoryTestFixture(RagDatabase db, MemoryService memory, string dir)
        {
            Db = db;
            Memory = memory;
            _dir = dir;
        }

        public RagDatabase Db { get; }
        public MemoryService Memory { get; }
        private readonly string _dir;

        public static Task<MemoryTestFixture> CreateAsync()
        {
            var relative = Path.Combine("obj", "lc-mem-" + Guid.NewGuid().ToString("N"));
            var opt = Options.Create(new LlamaOptions
            {
                DataDirectory = relative,
                LlamaServerBaseUrl = "http://127.0.0.1:9",
            });
            var db = new RagDatabase(opt);
            var store = new AppSettingsStore(db);
            store.Save(new AppSettingsDto { MemoryEnabled = true, MemoryAutoExtractOnClose = true });
            var models = new ModelCatalogService(AppPaths.Current, opt);
            var http = new HttpClient(new ImmediateFailHandler())
            {
                BaseAddress = new Uri("http://127.0.0.1:9/"),
            };
            var llama = new LlamaServerClient(
                http,
                opt,
                models,
                NullLogger<LlamaServerClient>.Instance);
            var memory = new MemoryService(db, llama, store, opt);
            return Task.FromResult(new MemoryTestFixture(db, memory, db.DataDirectory));
        }

        private sealed class ImmediateFailHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
            return ValueTask.CompletedTask;
        }
    }
}
