using System.Text.Json;
using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class RagService
{
    private const int MaxFolderFiles = 200;
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", "packages", "dist", "build", "runtimes",
    };

    private readonly RagDatabase _db;
    private readonly LlamaServerClient _llama;
    private readonly LlamaOptions _opt;
    private readonly SemaphoreSlim _ingestLock = new(1, 1);

    private const int MaxLegacySearchChunks = 3000;

    public RagService(RagDatabase db, LlamaServerClient llama, IOptions<LlamaOptions> opt)
    {
        _db = db;
        _llama = llama;
        _opt = opt.Value;
    }

    public async Task<int> IngestTextAsync(string source, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        if (!await _llama.EmbeddingsSupportedAsync(ct))
            throw new LocalizedServiceException("Settings.Rag.Error.EmbeddingsUnavailable");

        var drafts = RagStructuralChunker.CreateChunks(text, source, _opt.ChunkSize, _opt.ChunkOverlap);
        var prepared = new List<(RagChunkDraft Draft, float[] Embedding)>();

        foreach (var draft in drafts)
        {
            var emb = await _llama.EmbedAsync(draft.EmbeddingText, ct);
            if (emb is null || emb.Length == 0)
                continue;

            prepared.Add((draft, emb));
        }

        if (prepared.Count == 0)
            return 0;

        await _ingestLock.WaitAsync(ct);
        try
        {
            using var conn = _db.Open();
            await conn.OpenAsync(ct);
            _db.PrepareVectors(conn);

            var embeddingDim = prepared[0].Embedding.Length;

            await using var tx = await conn.BeginTransactionAsync(ct);
            var sqliteTx = (SqliteTransaction)tx;
            try
            {
                _db.Vector.EnsureVectorTable(conn, embeddingDim, sqliteTx);

                DeleteSourceChunks(conn, source, sqliteTx);

                var count = 0;
                foreach (var (draft, emb) in prepared)
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = sqliteTx;
                    cmd.CommandText = """
                        INSERT INTO rag_chunks (
                          source, text, embedding, created_at,
                          chunk_id, header_text, header_level, page, chapter, section, subsection
                        )
                        VALUES ($s, $t, $e, $at, $cid, $ht, $hl, $pg, $ch, $sec, $sub)
                        RETURNING id
                        """;
                    cmd.Parameters.AddWithValue("$s", source);
                    cmd.Parameters.AddWithValue("$t", draft.Text);
                    cmd.Parameters.AddWithValue("$e", JsonSerializer.Serialize(emb));
                    cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                    cmd.Parameters.AddWithValue("$cid", draft.ChunkId);
                    cmd.Parameters.AddWithValue("$ht", draft.HeaderText);
                    cmd.Parameters.AddWithValue("$hl", draft.HeaderLevel);
                    cmd.Parameters.AddWithValue("$pg", draft.Page);
                    cmd.Parameters.AddWithValue("$ch", draft.Chapter);
                    cmd.Parameters.AddWithValue("$sec", draft.Section);
                    cmd.Parameters.AddWithValue("$sub", draft.Subsection);

                    var idObj = await cmd.ExecuteScalarAsync(ct);
                    if (idObj is null)
                        continue;

                    var chunkId = Convert.ToInt64(idObj);
                    _db.Vector.InsertVector(conn, chunkId, emb, sqliteTx);
                    count++;
                }

                if (count == 0)
                {
                    await tx.RollbackAsync(ct);
                    return 0;
                }

                await tx.CommitAsync(ct);
                return count;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        finally
        {
            _ingestLock.Release();
        }
    }

    private void DeleteSourceChunks(SqliteConnection conn, string source, SqliteTransaction? transaction = null)
    {
        _db.Vector.DeleteVectorsForSource(conn, source, transaction);

        var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM rag_chunks WHERE source = $s";
        cmd.Parameters.AddWithValue("$s", source);
        cmd.ExecuteNonQuery();
    }

    public int DeleteSource(string source)
    {
        using var conn = _db.Open();
        _db.PrepareVectors(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            _db.Vector.DeleteVectorsForSource(conn, source, tx);

            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM rag_chunks WHERE source = $s";
            cmd.Parameters.AddWithValue("$s", source);
            var deleted = cmd.ExecuteNonQuery();

            var pref = conn.CreateCommand();
            pref.Transaction = tx;
            pref.CommandText = "DELETE FROM rag_source_prefs WHERE source = $s";
            pref.Parameters.AddWithValue("$s", source);
            pref.ExecuteNonQuery();

            tx.Commit();
            return deleted;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public bool IsSourceEnabled(string source)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enabled FROM rag_source_prefs WHERE source = $s";
        cmd.Parameters.AddWithValue("$s", source);
        var raw = cmd.ExecuteScalar();
        if (raw is null)
            return true;

        return Convert.ToInt32(raw) != 0;
    }

    public void SetSourceEnabled(string source, bool enabled)
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_source_prefs (source, enabled)
            VALUES ($s, $e)
            ON CONFLICT(source) DO UPDATE SET enabled = excluded.enabled
            """;
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<string> GetEnabledSources(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT c.source
            FROM rag_chunks c
            LEFT JOIN rag_source_prefs p ON p.source = c.source
            WHERE COALESCE(p.enabled, 1) = 1
            ORDER BY c.source
            """;
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public IReadOnlyList<RagSourceInfo> ListSources()
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.source, COUNT(*), MIN(c.created_at), COALESCE(p.enabled, 1)
            FROM rag_chunks c
            LEFT JOIN rag_source_prefs p ON p.source = c.source
            GROUP BY c.source
            ORDER BY MIN(c.created_at) DESC
            """;
        var list = new List<RagSourceInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var source = reader.GetString(0);
            var chunks = reader.GetInt32(1);
            var createdAt = reader.IsDBNull(2) ? null : reader.GetString(2);
            var exists = File.Exists(source);
            var enabled = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;
            list.Add(new RagSourceInfo(source, chunks, createdAt, exists, enabled));
        }
        return list;
    }

    public async Task<RagIngestResult> IngestSingleFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new LocalizedServiceException("Settings.Rag.Error.FileNotFound");

        var chunks = await IngestFileAsync(path, ct);
        return new RagIngestResult(path, 1, chunks, Array.Empty<string>());
    }

    public async Task<RagIngestResult> IngestPathAsync(string path, CancellationToken ct)
    {
        if (File.Exists(path))
            return await IngestSingleFileAsync(path, ct);

        if (Directory.Exists(path))
            return await IngestDirectoryAsync(path, ct);

        throw new LocalizedServiceException("Settings.Rag.Error.PathNotFound");
    }

    public async Task<RagIngestResult> IngestUploadedFilesAsync(
        IEnumerable<(string FileName, Stream Content)> files,
        CancellationToken ct)
    {
        var fileCount = 0;
        var chunkCount = 0;
        var skipped = new List<string>();

        foreach (var (fileName, content) in files)
        {
            if (!RagDocumentReader.IsSupported(fileName))
            {
                skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedUnsupported", fileName));
                continue;
            }

            try
            {
                var doc = RagDocumentReader.ReadDocument(content, fileName);
                var chunks = await IngestTextAsync(doc.Source, doc.Text, ct);
                if (chunks <= 0)
                {
                    skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedEmpty", fileName));
                    continue;
                }

                fileCount++;
                chunkCount += chunks;
            }
            catch (Exception ex)
            {
                skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedWithReason", fileName, UserFacingErrorLocalizer.Localize(ex)));
            }
        }

        return new RagIngestResult("upload", fileCount, chunkCount, skipped);
    }

    private async Task<int> IngestFileAsync(string path, CancellationToken ct)
    {
        if (!RagDocumentReader.IsSupported(path))
            throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", Path.GetExtension(path));

        var doc = RagDocumentReader.ReadDocument(path);
        return await IngestTextAsync(doc.Source, doc.Text, ct);
    }

    private async Task<RagIngestResult> IngestDirectoryAsync(string directory, CancellationToken ct)
    {
        var fileCount = 0;
        var chunkCount = 0;
        var skipped = new List<string>();

        foreach (var file in EnumerateIngestFiles(directory))
        {
            if (fileCount >= MaxFolderFiles)
            {
                skipped.Add(LocalizationService.Instance.Format("Settings.Rag.Error.SkippedFolderLimit", MaxFolderFiles));
                break;
            }

            try
            {
                var chunks = await IngestFileAsync(file, ct);
                if (chunks <= 0)
                {
                    skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedZeroChunks", file));
                    continue;
                }

                fileCount++;
                chunkCount += chunks;
            }
            catch (Exception ex)
            {
                skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedWithReason", file, UserFacingErrorLocalizer.Localize(ex)));
            }
        }

        return new RagIngestResult(directory, fileCount, chunkCount, skipped);
    }

    public int GetChunkCount()
    {
        using var conn = _db.Open();
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM rag_chunks";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public async Task<IReadOnlyList<RagSearchHit>> SearchAsync(string query, int topK, CancellationToken ct)
    {
        if (GetChunkCount() == 0)
            return Array.Empty<RagSearchHit>();

        if (!await _llama.EmbeddingsSupportedAsync(ct))
            return Array.Empty<RagSearchHit>();

        var q = await _llama.EmbedAsync(query, ct);
        if (q is null || q.Length == 0)
            return Array.Empty<RagSearchHit>();

        using var conn = _db.Open();
        await conn.OpenAsync(ct);
        _db.PrepareVectors(conn);

        var enabledSources = GetEnabledSources(conn);
        if (enabledSources.Count == 0)
            return Array.Empty<RagSearchHit>();

        if (_db.Vector.IsAvailable)
        {
            _db.Vector.EnsureVectorTable(conn, q.Length);
            var ids = _db.Vector.Search(conn, q, topK, enabledSources);
            if (ids.Count > 0)
                return LoadHitsByIds(conn, ids);
        }

        return await LegacySearchAsync(conn, q, topK, enabledSources, ct);
    }

    private static IReadOnlyList<RagSearchHit> LoadHitsByIds(SqliteConnection conn, IReadOnlyList<long> ids)
    {
        var hits = new List<RagSearchHit>(ids.Count);
        foreach (var id in ids)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT text, source, header_text, page, chunk_id
                FROM rag_chunks WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                continue;

            var text = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            hits.Add(new RagSearchHit(
                text,
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return hits;
    }

    private static async Task<IReadOnlyList<RagSearchHit>> LegacySearchAsync(
        SqliteConnection conn,
        float[] q,
        int topK,
        IReadOnlyList<string> enabledSources,
        CancellationToken ct)
    {
        var cmd = conn.CreateCommand();
        var inClause = BuildInClause(cmd, enabledSources, "src");
        var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"""
            SELECT COUNT(*)
            FROM rag_chunks
            WHERE source IN ({inClause})
            """;
        foreach (SqliteParameter p in cmd.Parameters)
            countCmd.Parameters.AddWithValue(p.ParameterName, p.Value);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        if (total > MaxLegacySearchChunks)
            return Array.Empty<RagSearchHit>();

        var rows = new List<(RagSearchHit Hit, float[] Vec)>();
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, embedding
            FROM rag_chunks
            WHERE source IN ({inClause})
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var text = reader.GetString(0);
            var vec = JsonSerializer.Deserialize<float[]>(reader.GetString(5));
            if (vec is not { Length: > 0 } || string.IsNullOrWhiteSpace(text))
                continue;

            var hit = new RagSearchHit(
                text,
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4));
            rows.Add((hit, vec));
        }

        return rows
            .Select(r => (r.Hit, Score: CosineSimilarity(q, r.Vec)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Hit)
            .ToList();
    }

    private static string FormatSkipped(string key, params object[] args) =>
        LocalizationService.Instance.Format(key, args);

    public sealed record RagIngestResult(string Path, int Files, int Chunks, IReadOnlyList<string> Skipped);

    public sealed record RagSourceInfo(string Source, int Chunks, string? CreatedAt, bool FileExists, bool Enabled);

    private static string BuildInClause(SqliteCommand cmd, IReadOnlyList<string> values, string prefix)
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

    private static IEnumerable<string> EnumerateIngestFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!RagDocumentReader.IsSupported(file))
                continue;
            if (ShouldSkipIngestPath(file))
                continue;
            yield return file;
        }
    }

    private static bool ShouldSkipIngestPath(string filePath)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirNames.Contains(p));
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}
