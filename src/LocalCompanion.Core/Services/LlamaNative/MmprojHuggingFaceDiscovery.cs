using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services.LlamaNative;

internal static class MmprojHuggingFaceDiscovery
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Regex MmprojRemote = new(
        @"(?i)(^|/)mmproj.*\.gguf$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GenericRemoteMmproj = new(
        @"^(mmproj-(F16|BF16|f16|bf16)|mmproj-model-[A-Za-z0-9_]+)\.gguf$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal readonly record struct ListingHit(string RemoteRelativePath, string LocalName);

    internal static async Task<MmprojFamilyRegistry.MmprojDownloadSpec?> TryDiscoverAsync(
        string root,
        string modelFileName,
        MmprojFamilyRegistry.MatchedFamily? matched,
        CancellationToken ct)
    {
        var seenRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in MmprojFamilyRegistry.GetHfSearchTerms(modelFileName, root))
        {
            var spec = await SearchTermAsync(term, modelFileName, matched, seenRepos, root, ct);
            if (spec is not null)
                return spec;
        }

        return null;
    }

    private static async Task<MmprojFamilyRegistry.MmprojDownloadSpec?> SearchTermAsync(
        string term,
        string modelFileName,
        MmprojFamilyRegistry.MatchedFamily? matched,
        HashSet<string> seenRepos,
        string root,
        CancellationToken ct)
    {
        var searchUrl =
            $"https://huggingface.co/api/models?search={Uri.EscapeDataString(term)}&limit=12";
        JsonElement[] results;
        try
        {
            results = await Http.GetFromJsonAsync<JsonElement[]>(searchUrl, ct) ?? [];
        }
        catch
        {
            return null;
        }

        var ranked = results
            .Select(hit => new
            {
                RepoId = hit.TryGetProperty("id", out var idEl) ? idEl.GetString()
                    : hit.TryGetProperty("modelId", out var mid) ? mid.GetString() : null,
                Score = ScoreRepo(hit, root, modelFileName),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.RepoId))
            .OrderByDescending(x => x.Score)
            .ToList();

        foreach (var item in ranked)
        {
            if (item.RepoId is null || !seenRepos.Add(item.RepoId))
                continue;

            var spec = await TryRepoAsync(item.RepoId, modelFileName, matched, ct);
            if (spec is not null)
                return spec;
        }

        return null;
    }

    private static int ScoreRepo(JsonElement hit, string root, string modelFileName)
    {
        var repoId = hit.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        return ScoreRepoId(repoId, modelFileName, MmprojFamilyRegistry.GetRepoPriorityPrefixes(root));
    }

    internal static int ScoreRepoId(
        string repoId,
        string modelFileName,
        IReadOnlyList<string> priorityPrefixes)
    {
        var score = 0;
        foreach (var prefix in priorityPrefixes)
        {
            if (repoId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
                break;
            }
        }

        if (repoId.Contains("GGUF", StringComparison.OrdinalIgnoreCase))
            score += 10;

        var stem = MmprojFamilyRegistry.NormalizeModelStem(modelFileName);
        if (string.IsNullOrWhiteSpace(stem))
            return score;

        var slash = repoId.LastIndexOf('/');
        var leaf = slash >= 0 ? repoId[(slash + 1)..] : repoId;
        if (leaf.Equals(stem, StringComparison.OrdinalIgnoreCase)
            || leaf.Equals(stem + "-GGUF", StringComparison.OrdinalIgnoreCase))
            score += 80;
        else if (leaf.Contains(stem, StringComparison.OrdinalIgnoreCase)
            || repoId.Contains(stem, StringComparison.OrdinalIgnoreCase))
            score += 40;

        var baseName = Path.GetFileNameWithoutExtension(modelFileName);
        if (!string.IsNullOrWhiteSpace(baseName)
            && repoId.Contains(baseName, StringComparison.OrdinalIgnoreCase))
            score += 20;

        return score;
    }

    private static async Task<MmprojFamilyRegistry.MmprojDownloadSpec?> TryRepoAsync(
        string repoId,
        string modelFileName,
        MmprojFamilyRegistry.MatchedFamily? matched,
        CancellationToken ct)
    {
        JsonElement detail;
        try
        {
            detail = await Http.GetFromJsonAsync<JsonElement>(
                $"https://huggingface.co/api/models/{repoId}", ct);
        }
        catch
        {
            return null;
        }

        if (!detail.TryGetProperty("siblings", out var siblings) || siblings.ValueKind != JsonValueKind.Array)
            return null;

        var files = siblings.EnumerateArray()
            .Select(s => s.TryGetProperty("rfilename", out var rf) ? rf.GetString() : null)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .ToList();

        var hit = TryPickFromListing(modelFileName, repoId, files, matched);
        if (hit is null)
            return null;

        return new MmprojFamilyRegistry.MmprojDownloadSpec(
            $"https://huggingface.co/{repoId}/resolve/main/{hit.Value.RemoteRelativePath}",
            hit.Value.LocalName,
            repoId,
            $"HF: {repoId}");
    }

    internal static ListingHit? TryPickFromListing(
        string modelFileName,
        string repoId,
        IReadOnlyList<string> siblingFiles,
        MmprojFamilyRegistry.MatchedFamily? matched)
    {
        if (siblingFiles.Count == 0)
            return null;

        var mmprojs = siblingFiles.Where(f => MmprojRemote.IsMatch(f)).ToList();
        if (mmprojs.Count == 0)
            return null;

        if (matched is null)
        {
            var hasExactModel = siblingFiles.Any(f =>
                string.Equals(Path.GetFileName(f), modelFileName, StringComparison.OrdinalIgnoreCase));
            if (!hasExactModel)
                return null;
        }

        var picked = PickRemoteMmproj(modelFileName, mmprojs, matched);
        if (picked is null)
            return null;

        var remoteBase = Path.GetFileName(picked);
        if (string.IsNullOrWhiteSpace(remoteBase))
            return null;

        var localName = ResolveLocalName(modelFileName, remoteBase, repoId, matched);
        return new ListingHit(picked, localName);
    }

    private static string? PickRemoteMmproj(
        string modelFileName,
        IReadOnlyList<string> remoteFiles,
        MmprojFamilyRegistry.MatchedFamily? matched)
    {
        var names = remoteFiles.Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
        foreach (var prefer in MmprojFamilyRegistry.GetRemotePrefer(matched))
        {
            var hit = names.FirstOrDefault(n => string.Equals(n, prefer, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return remoteFiles.First(f => string.Equals(Path.GetFileName(f), hit, StringComparison.OrdinalIgnoreCase));
        }

        if (matched is not null)
        {
            var token = matched.SizeToken;
            var byToken = names.FirstOrDefault(n => n.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (byToken is not null)
                return remoteFiles.First(f => string.Equals(Path.GetFileName(f), byToken, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var generic in new[]
                 {
                     "mmproj-BF16.gguf", "mmproj-F16.gguf", "mmproj-f16.gguf", "mmproj-bf16.gguf",
                     "mmproj-model-bf16.gguf", "mmproj-model-f16.gguf",
                 })
        {
            var hit = names.FirstOrDefault(n => string.Equals(n, generic, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return remoteFiles.First(f => string.Equals(Path.GetFileName(f), hit, StringComparison.OrdinalIgnoreCase));
        }

        var modelNamed = names.FirstOrDefault(n => GenericRemoteMmproj.IsMatch(n));
        if (modelNamed is not null)
            return remoteFiles.First(f => string.Equals(Path.GetFileName(f), modelNamed, StringComparison.OrdinalIgnoreCase));

        return remoteFiles.FirstOrDefault();
    }

    private static string ResolveLocalName(
        string modelFileName,
        string remoteBase,
        string repoId,
        MmprojFamilyRegistry.MatchedFamily? matched)
    {
        var canonical = matched?.Entry.Mmproj?.LocalName;
        if (!string.IsNullOrWhiteSpace(canonical))
            return canonical;

        if (!IsGenericRemoteMmprojName(remoteBase))
            return remoteBase;

        var stem = MmprojFamilyRegistry.NormalizeModelStem(modelFileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            var slug = repoId.Split('/')[^1].Replace('.', '-');
            slug = Regex.Replace(slug, @"[^\w\-]", "-");
            return $"mmproj-{slug}.gguf";
        }

        var safe = Regex.Replace(stem, @"[^\w.\-]", "-");
        return $"mmproj-{safe}.gguf";
    }

    private static bool IsGenericRemoteMmprojName(string remoteBase) =>
        GenericRemoteMmproj.IsMatch(remoteBase);
}
