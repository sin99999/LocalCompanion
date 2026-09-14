using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Core.Tests;

public sealed class ChatHistoryDigestWriterTests
{
    [Fact]
    public async Task TryUpdateAsync_SameFrontier_DoesNotCallLlm()
    {
        await using var fx = await DigestFixture.CreateAsync();
        fx.Llm.Reply = "should-not-write";
        fx.Store.Write(fx.SessionId, "既存", 10);

        await fx.Writer.TryUpdateAsync(
            fx.SessionId,
            [new ChatHistoryRow(9, "user", "古い"), new ChatHistoryRow(10, "assistant", "返事")],
            CancellationToken.None);

        Assert.Equal(0, fx.Llm.Calls);
        var (digest, upto) = fx.Store.Read(fx.SessionId);
        Assert.Equal("既存", digest);
        Assert.Equal(10, upto);
    }

    [Fact]
    public async Task TryUpdateAsync_NewDropped_WritesClampedDigest()
    {
        await using var fx = await DigestFixture.CreateAsync();
        fx.Llm.Reply = "  要約本文  ";

        await fx.Writer.TryUpdateAsync(
            fx.SessionId,
            [new ChatHistoryRow(1, "user", "昔の話"), new ChatHistoryRow(2, "assistant", "うん")],
            CancellationToken.None);

        Assert.Equal(1, fx.Llm.Calls);
        var (digest, upto) = fx.Store.Read(fx.SessionId);
        Assert.Equal("要約本文", digest);
        Assert.Equal(2, upto);
    }

    [Fact]
    public async Task TryUpdateAsync_LlmThrows_LeavesColumnsUnchanged()
    {
        await using var fx = await DigestFixture.CreateAsync();
        fx.Store.Write(fx.SessionId, "既存", 1);
        fx.Llm.Error = new InvalidOperationException("llama down");

        await fx.Writer.TryUpdateAsync(
            fx.SessionId,
            [new ChatHistoryRow(2, "user", "新しい落ち")],
            CancellationToken.None);

        var (digest, upto) = fx.Store.Read(fx.SessionId);
        Assert.Equal("既存", digest);
        Assert.Equal(1, upto);
    }

    private sealed class FakeDigestLlm : IChatHistoryDigestLlm
    {
        public int Calls { get; private set; }
        public string? Reply { get; set; } = "ok";
        public Exception? Error { get; set; }

        public Task<string?> SummarizeAsync(string existingDigest, string droppedTranscript, CancellationToken ct)
        {
            Calls++;
            if (Error is not null)
                throw Error;
            return Task.FromResult(Reply);
        }
    }

    private sealed class DigestFixture : IAsyncDisposable
    {
        public required string Dir { get; init; }
        public required string SessionId { get; init; }
        public required ChatHistoryDigestStore Store { get; init; }
        public required ChatHistoryDigestWriter Writer { get; init; }
        public required FakeDigestLlm Llm { get; init; }

        public static Task<DigestFixture> CreateAsync()
        {
            var relative = Path.Combine("obj", "lc-test-" + Guid.NewGuid().ToString("N"));
            var db = new RagDatabase(Options.Create(new LlamaOptions { DataDirectory = relative }));
            var sessionId = Guid.NewGuid().ToString("N");
            using (var conn = db.Open())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO conversation_sessions (id, preset_key, title, summary, created_at, updated_at)
                    VALUES ($id, 'example.json', '', '', $t, $t)
                    """;
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            var store = new ChatHistoryDigestStore(db);
            var llm = new FakeDigestLlm();
            return Task.FromResult(new DigestFixture
            {
                Dir = db.DataDirectory,
                SessionId = sessionId,
                Store = store,
                Writer = new ChatHistoryDigestWriter(store, llm),
                Llm = llm,
            });
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(Dir, true); } catch { /* ignore */ }
            return ValueTask.CompletedTask;
        }
    }
}
