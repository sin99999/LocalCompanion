using System.Text.Json;
using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class RagService
{
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", "packages", "dist", "build", "runtimes",
    };

    private readonly RagDatabase _db;
    private readonly LlamaServerClient _llama;
    private readonly RagDocumentStructurer _structurer;
    private readonly AppSettingsStore _settings;
    private readonly LlamaOptions _opt;
    private readonly SemaphoreSlim _ingestLock = new(1, 1);

    private const int MaxLegacySearchChunks = 3000;

    public RagService(
        RagDatabase db,
        LlamaServerClient llama,
        RagDocumentStructurer structurer,
        AppSettingsStore settings,
        IOptions<LlamaOptions> opt)
    {
        _db = db;
        _llama = llama;
        _structurer = structurer;
        _settings = settings;
        _opt = opt.Value;
    }

    public async Task<(int ChunkCount, int EmbedSkipped, RagIngestStats Stats)> IngestTextAsync(string source, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (0, 0, RagIngestStats.Empty);

        if (!await _llama.EmbeddingsSupportedAsync(ct))
            throw new LocalizedServiceException("Settings.Rag.Error.EmbeddingsUnavailable");

        EnsureReaderConfigured();
        var options = LoadIngestOptions();
        text = RagIngestPreprocessor.Preprocess(source, text, options);
        text = RagDocumentNormalizer.Normalize(text);
        var docKind = RagDocumentProfileDetector.Detect(source, text);
        text = await _structurer.StructureAsync(source, text, docKind, options, ct);
        text = RagDocumentNormalizer.Normalize(text);
        var drafts = RagStructuralChunker.CreateChunks(text, source, _opt.ChunkSize, _opt.ChunkOverlap, docKind);
        var stats = BuildIngestStats(drafts, docKind, 0);
        var prepared = new List<(RagChunkDraft Draft, float[]? Embedding)>();
        var embedSkipped = 0;

        foreach (var draft in drafts)
        {
            var emb = await _llama.EmbedAsync(draft.EmbeddingText, ct);
            if (emb is null || emb.Length == 0)
            {
                embedSkipped++;
                prepared.Add((draft, null));
                continue;
            }

            prepared.Add((draft, emb));
        }

        if (prepared.Count == 0)
            return (0, embedSkipped, stats with { EmbedSkipped = embedSkipped });

        await _ingestLock.WaitAsync(ct);
        try
        {
            using var conn = _db.Open();
            await conn.OpenAsync(ct);
            _db.PrepareVectors(conn);
            _db.PrepareFts(conn);

            var firstEmb = prepared.Select(p => p.Embedding).FirstOrDefault(e => e is { Length: > 0 });
            var embeddingDim = firstEmb?.Length ?? 0;

            await using var tx = await conn.BeginTransactionAsync(ct);
            var sqliteTx = (SqliteTransaction)tx;
            try
            {
                if (embeddingDim > 0)
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
                          chunk_id, header_text, header_level, page, chapter, section, subsection, parent_text,
                          article_main, article_sub, article_sort_key, penalty_lead, chunk_kind,
                          entry_key, definition_lead, section_path, doc_kind
                        )
                        VALUES ($s, $t, $e, $at, $cid, $ht, $hl, $pg, $ch, $sec, $sub, $parent,
                          $am, $as, $ask, $pl, $ck, $ek, $dl, $sp, $dk)
                        RETURNING id
                        """;
                    cmd.Parameters.AddWithValue("$s", source);
                    cmd.Parameters.AddWithValue("$t", draft.Text);
                    cmd.Parameters.AddWithValue("$e", emb is { Length: > 0 } ? JsonSerializer.Serialize(emb) : "[]");
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

                    var idObj = await cmd.ExecuteScalarAsync(ct);
                    if (idObj is null)
                        continue;

                    var chunkId = Convert.ToInt64(idObj);
                    if (emb is { Length: > 0 })
                        _db.Vector.InsertVector(conn, chunkId, emb, sqliteTx);
                    _db.Fts.IndexChunk(
                        conn,
                        chunkId,
                        draft.Text,
                        draft.HeaderText,
                        draft.Chapter,
                        draft.Section,
                        sqliteTx);
                    count++;
                }

                if (count == 0)
                {
                    await tx.RollbackAsync(ct);
                    return (0, embedSkipped, stats with { EmbedSkipped = embedSkipped });
                }

                await tx.CommitAsync(ct);
                return (count, embedSkipped, stats with { EmbedSkipped = embedSkipped, TotalChunks = count });
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
        _db.Fts.DeleteForSource(conn, source, transaction);

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
        _db.PrepareFts(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            _db.Vector.DeleteVectorsForSource(conn, source, tx);
            _db.Fts.DeleteForSource(conn, source, tx);

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
        return ListSources(conn);
    }

    public async Task<RagIngestResult> IngestUrlAsync(string url, CancellationToken ct)
    {
        var (displayName, text) = await ChatUrlContentFetcher.FetchAsync(url, ct);
        var source = $"url:{url.Trim()}";
        var (chunks, embedSkipped, stats) = await IngestTextAsync(source, text, ct);
        var skipped = BuildEmbedWarnings(displayName, embedSkipped);
        if (chunks <= 0)
            skipped = [.. skipped, FormatSkipped("Settings.Rag.Error.SkippedEmpty", displayName)];
        return new RagIngestResult(source, chunks > 0 ? 1 : 0, chunks, skipped, stats);
    }

    public async Task<RagIngestResult> IngestSingleFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new LocalizedServiceException("Settings.Rag.Error.FileNotFound");

        var (chunks, _, skipped, stats) = await IngestFileDetailedAsync(path, ct);
        return new RagIngestResult(path, chunks > 0 ? 1 : 0, chunks, skipped, stats);
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
        EnsureReaderConfigured();
        var fileCount = 0;
        var chunkCount = 0;
        var skipped = new List<string>();
        RagIngestStats? lastStats = null;

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
                var (chunks, embedSkipped, stats) = await IngestTextAsync(doc.Source, doc.Text, ct);
                var warnings = BuildEmbedWarnings(fileName, embedSkipped);
                warnings = [.. warnings, .. BuildLegalIngestWarnings(fileName, stats)];
                if (chunks <= 0)
                {
                    skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedEmpty", fileName));
                    skipped.AddRange(warnings);
                    continue;
                }

                fileCount++;
                chunkCount += chunks;
                skipped.AddRange(warnings);
                lastStats = stats;
            }
            catch (Exception ex)
            {
                skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedWithReason", fileName, UserFacingErrorLocalizer.Localize(ex)));
            }
        }

        return new RagIngestResult("upload", fileCount, chunkCount, skipped, lastStats);
    }

    private async Task<(int Chunks, int EmbedSkipped, IReadOnlyList<string> Skipped, RagIngestStats Stats)> IngestFileDetailedAsync(
        string path,
        CancellationToken ct)
    {
        EnsureReaderConfigured();
        if (!RagDocumentReader.IsSupported(path))
            throw new LocalizedServiceException("Settings.Rag.Error.UnsupportedFormat", Path.GetExtension(path));

        var doc = RagDocumentReader.ReadDocument(path);
        var (chunks, embedSkipped, stats) = await IngestTextAsync(doc.Source, doc.Text, ct);
        var skipped = BuildEmbedWarnings(path, embedSkipped);
        skipped = [.. skipped, .. BuildLegalIngestWarnings(path, stats)];
        return (chunks, embedSkipped, skipped, stats);
    }

    private IReadOnlyList<string> BuildLegalIngestWarnings(string pathOrName, RagIngestStats stats)
    {
        if (!string.Equals(stats.DocKind, "Legal", StringComparison.OrdinalIgnoreCase)
            || stats.ArticleChunks > 0
            || stats.TotalChunks <= 0)
        {
            return Array.Empty<string>();
        }

        return [FormatSkipped("Settings.Rag.Error.SkippedNoArticles", Path.GetFileName(pathOrName))];
    }

    private IReadOnlyList<string> BuildEmbedWarnings(string pathOrName, int embedSkipped)
    {
        if (embedSkipped <= 0)
            return Array.Empty<string>();

        var label = Path.GetFileName(pathOrName);
        return [FormatSkipped("Settings.Rag.Error.SkippedPartialEmbed", label, embedSkipped)];
    }

    private async Task<RagIngestResult> IngestDirectoryAsync(string directory, CancellationToken ct)
    {
        EnsureReaderConfigured();
        var fileCount = 0;
        var chunkCount = 0;
        var skipped = new List<string>();
        RagIngestStats? lastDirStats = null;

        var folderLimit = _opt.RagMaxFolderFiles;
        foreach (var file in EnumerateIngestFiles(directory))
        {
            if (folderLimit > 0 && fileCount >= folderLimit)
            {
                skipped.Add(LocalizationService.Instance.Format("Settings.Rag.Error.SkippedFolderLimit", folderLimit));
                break;
            }

            try
            {
                var (chunks, _, warnings, stats) = await IngestFileDetailedAsync(file, ct);
                if (chunks <= 0)
                {
                    skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedZeroChunks", file));
                    skipped.AddRange(warnings);
                    continue;
                }

                fileCount++;
                chunkCount += chunks;
                skipped.AddRange(warnings);
                lastDirStats = stats;
            }
            catch (Exception ex)
            {
                skipped.Add(FormatSkipped("Settings.Rag.Error.SkippedWithReason", file, UserFacingErrorLocalizer.Localize(ex)));
            }
        }

        return new RagIngestResult(directory, fileCount, chunkCount, skipped, lastDirStats);
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
        var result = await SearchWithPlanAsync(query, previousUserMessage: null, topK, ct);
        return result.Hits;
    }

    public async Task<RagSearchResult> SearchWithPlanAsync(
        string currentMessage,
        string? previousUserMessage,
        int topK,
        CancellationToken ct)
    {
        var plan = RagQueryPlanner.Plan(currentMessage, previousUserMessage);
        if (GetChunkCount() == 0)
            return new RagSearchResult(Array.Empty<RagSearchHit>(), plan);

        if (!await _llama.EmbeddingsSupportedAsync(ct))
            return new RagSearchResult(Array.Empty<RagSearchHit>(), plan);

        using var conn = _db.Open();
        await conn.OpenAsync(ct);
        _db.PrepareVectors(conn);
        _db.PrepareFts(conn);

        var enabledSources = GetEnabledSources(conn);
        if (enabledSources.Count == 0)
            return new RagSearchResult(Array.Empty<RagSearchHit>(), plan);

        plan = RagSourceHintResolver.EnrichPlan(plan, enabledSources);

        var sources = string.IsNullOrWhiteSpace(plan.SourceHint)
            ? enabledSources
            : FilterSourcesByHint(enabledSources, plan.SourceHint);

        if (plan.Intent == RagQueryIntent.Advisory && plan.SourceHints is { Count: > 0 })
        {
            var advisoryHits = await MultiSourceHybridSearchAsync(
                conn, plan.EffectiveQuery, plan.SourceHints, enabledSources, Math.Max(topK, 8), ct);
            if (advisoryHits.Count > 0)
                return new RagSearchResult(CollapseParentHits(advisoryHits).Take(topK + 2).ToList(), plan);
        }

        if (plan.Intent == RagQueryIntent.SourceCatalog)
        {
            var catalogHits = BuildSourceCatalogHits(ListSources(conn));
            return new RagSearchResult(catalogHits, plan);
        }

        if (plan.Intent != RagQueryIntent.General && plan.Intent != RagQueryIntent.Advisory)
        {
            var structured = CollapseParentHits(RagStructuredSearch.Execute(conn, plan, sources, topK));
            if (structured.Count > 0)
                return new RagSearchResult(structured.Take(topK).ToList(), plan);
        }

        var hybridSources = sources.Count > 0 && sources.Count < enabledSources.Count
            ? sources
            : enabledSources;
        var hybridHits = await HybridSearchAsync(conn, plan.EffectiveQuery, hybridSources, topK, ct);
        return new RagSearchResult(CollapseParentHits(hybridHits).Take(topK).ToList(), plan);
    }

    private static IReadOnlyList<RagSourceInfo> ListSources(SqliteConnection conn)
    {
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

    private static IReadOnlyList<RagSearchHit> BuildSourceCatalogHits(IReadOnlyList<RagSourceInfo> sources)
    {
        var loc = LocalizationService.Instance;
        var hits = new List<RagSearchHit>(sources.Count);
        foreach (var source in sources)
        {
            var label = FormatSourceDisplayName(source.Source);
            var line = source.Enabled
                ? loc.Format("Settings.Rag.Catalog.Line", label, source.Chunks)
                : loc.Format("Settings.Rag.Catalog.LineDisabled", label, source.Chunks);
            hits.Add(new RagSearchHit(
                line,
                source.Source,
                label,
                0,
                "__catalog__",
                "",
                "",
                "",
                ""));
        }

        return hits;
    }

    private static string FormatSourceDisplayName(string source) =>
        RagSourceLabel.Format(source) is var label && !string.IsNullOrWhiteSpace(label)
            ? (label.Contains('/') || label.Contains('\\')
                ? Path.GetFileName(label.TrimEnd('/', '\\'))
                : label)
            : source;

    private async Task<IReadOnlyList<RagSearchHit>> HybridSearchAsync(
        SqliteConnection conn,
        string query,
        IReadOnlyList<string> enabledSources,
        int topK,
        CancellationToken ct)
    {
        var q = await _llama.EmbedAsync(query, ct);
        if (q is null || q.Length == 0)
            return Array.Empty<RagSearchHit>();

        var pool = Math.Max(_opt.RagSearchPoolSize, topK);
        var rrfK = Math.Max(_opt.RagRrfK, 1);
        var (wFts, wVec) = RagHybridSearch.ResolveWeights(query, _opt.RagWeightFts, _opt.RagWeightVec);

        IReadOnlyList<long> ftsIds = Array.Empty<long>();
        if (_db.Fts.IsAvailable)
        {
            var matchQuery = BuildFtsMatchQuery(query);
            if (!string.IsNullOrWhiteSpace(matchQuery))
                ftsIds = _db.Fts.Search(conn, matchQuery, pool, enabledSources);
        }

        IReadOnlyList<long> vectorIds = Array.Empty<long>();
        if (_db.Vector.IsAvailable)
        {
            _db.Vector.EnsureVectorTable(conn, q.Length);
            vectorIds = _db.Vector.Search(conn, q, pool, enabledSources);
        }

        if (ftsIds.Count > 0 || vectorIds.Count > 0)
        {
            var fused = RagHybridSearch.FuseRrf(ftsIds, vectorIds, topK, rrfK, wFts, wVec);
            if (fused.Count > 0)
                return LoadHitsByIds(conn, fused);
        }

        return await LegacySearchAsync(conn, q, topK, enabledSources, ct);
    }

    private async Task<IReadOnlyList<RagSearchHit>> MultiSourceHybridSearchAsync(
        SqliteConnection conn,
        string query,
        IReadOnlyList<string> sourceHints,
        IReadOnlyList<string> enabledSources,
        int topK,
        CancellationToken ct)
    {
        var perSource = Math.Clamp((topK + sourceHints.Count - 1) / sourceHints.Count, 2, 4);
        var merged = new List<RagSearchHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hint in sourceHints)
        {
            var sources = FilterSourcesByHint(enabledSources, hint);
            var hits = await HybridSearchAsync(conn, query, sources, perSource, ct);
            foreach (var hit in hits)
            {
                var key = hit.Source + "\0" + (hit.ChunkId.Length > 0 ? hit.ChunkId : hit.Text[..Math.Min(40, hit.Text.Length)]);
                if (!seen.Add(key))
                    continue;
                merged.Add(hit);
            }
        }

        if (merged.Count >= topK / 2)
            return merged;

        var fallback = await HybridSearchAsync(conn, query, enabledSources, topK, ct);
        foreach (var hit in fallback)
        {
            var key = hit.Source + "\0" + hit.ChunkId;
            if (seen.Add(key))
                merged.Add(hit);
        }

        return merged;
    }

    private static string BuildFtsMatchQuery(string query)
    {
        var match = RagSqliteFts.BuildMatchQuery(query);
        if (!RagArticleQueryParser.TryGetArticleNumber(query, out var articleNumber))
            return match;

        var articleTerms = RagArticleQueryParser.BuildHeaderPrefixes(articleNumber)
            .Select(p => $"\"{p}\"");
        if (string.IsNullOrWhiteSpace(match))
            return string.Join(" OR ", articleTerms);

        return match + " OR " + string.Join(" OR ", articleTerms);
    }

    private static IReadOnlyList<RagSearchHit> CollapseParentHits(IReadOnlyList<RagSearchHit> hits)
    {
        var result = new List<RagSearchHit>(hits.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            var key = !string.IsNullOrWhiteSpace(hit.ParentText)
                ? hit.Source + "\0" + hit.ParentText
                : hit.Source + "\0" + hit.ChunkId + "\0" + hit.Text;
            if (!seen.Add(key))
                continue;
            result.Add(hit);
        }

        return result;
    }

    private static IReadOnlyList<RagSearchHit> LoadHitsByIds(SqliteConnection conn, IReadOnlyList<long> ids)
    {
        var hits = new List<RagSearchHit>(ids.Count);
        foreach (var id in ids)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead
                FROM rag_chunks WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                continue;

            var text = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            hits.Add(MapSearchHit(reader));
        }
        return hits;
    }

    private static RagSearchHit MapSearchHit(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var penaltyLead = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : "";
        var definitionLead = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "";
        return new RagSearchHit(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            reader.IsDBNull(5) ? "" : reader.GetString(5),
            penaltyLead,
            penaltyLead,
            definitionLead);
    }

    private static IReadOnlyList<string> FilterSourcesByHint(
        IReadOnlyList<string> enabledSources,
        string? sourceHint)
    {
        if (string.IsNullOrWhiteSpace(sourceHint))
            return enabledSources;

        var filtered = enabledSources
            .Where(s => SourceMatchesHint(s, sourceHint))
            .ToList();
        return filtered.Count > 0 ? filtered : enabledSources;
    }

    private static bool SourceMatchesHint(string source, string sourceHint)
    {
        if (string.IsNullOrWhiteSpace(sourceHint))
            return true;

        if (source.Contains(sourceHint, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(source);
        if (fileName.Contains(sourceHint, StringComparison.OrdinalIgnoreCase))
            return true;

        var label = RagSourceLabel.Format(source);
        return label.Contains(sourceHint, StringComparison.OrdinalIgnoreCase);
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
        // 大規模 DB では全件ロードせず、新しいチャンクから上限件数だけ評価する（空返しで黙って失敗しない）
        var rows = new List<(RagSearchHit Hit, float[] Vec)>();
        cmd.CommandText = $"""
            SELECT text, source, header_text, page, chunk_id, parent_text, penalty_lead, definition_lead, embedding
            FROM rag_chunks
            WHERE source IN ({inClause})
            ORDER BY id DESC
            LIMIT {MaxLegacySearchChunks}
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var text = reader.GetString(0);
            var vec = JsonSerializer.Deserialize<float[]>(reader.GetString(8));
            if (vec is not { Length: > 0 } || string.IsNullOrWhiteSpace(text))
                continue;

            rows.Add((MapSearchHit(reader), vec));
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

    public sealed record RagIngestResult(
        string Path,
        int Files,
        int Chunks,
        IReadOnlyList<string> Skipped,
        RagIngestStats? Stats = null);

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

    private static RagIngestStats BuildIngestStats(
        IReadOnlyList<RagChunkDraft> drafts,
        RagDocumentKind docKind,
        int embedSkipped)
    {
        var definition = 0;
        var faq = 0;
        var article = 0;
        foreach (var draft in drafts)
        {
            if (draft.ChunkKind is "definition" or "glossary")
                definition++;
            else if (draft.ChunkKind == "faq")
                faq++;
            if (draft.ArticleSortKey > 0)
                article++;
        }

        return new RagIngestStats(
            docKind.ToString(),
            drafts.Count,
            definition,
            faq,
            article,
            embedSkipped);
    }

    private RagIngestOptions LoadIngestOptions()
    {
        var s = _settings.Load();
        return new RagIngestOptions(
            s.RagUseHtmlMarkdown,
            s.RagUseLlmStructurer,
            s.RagSaveStructurerCache,
            s.RagUsePdfLayoutReader);
    }

    private void EnsureReaderConfigured()
    {
        var options = LoadIngestOptions();
        RagDocumentReader.Configure(options.UsePdfLayoutReader, _opt.RagMaxFileBytes);
    }
}
