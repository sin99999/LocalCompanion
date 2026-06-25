using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services.LlamaNative;

internal static class MmprojFamilyRegistry
{
    private static readonly Regex QuantSuffix = new(
        @"-(?:UD-|IQ)?(?:Q[0-9][\w_.-]*|F16|F32|BF16|fp16|QAT|qat)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record MmprojDownloadSpec(string Url, string LocalName, string? RepoId, string Label);

    internal sealed record MatchedFamily(FamilyEntry Entry, string SizeToken);

    private sealed class RegistryDocument
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("repoPriorityPrefixes")]
        public string[] RepoPriorityPrefixes { get; init; } = [];

        [JsonPropertyName("families")]
        public FamilyEntry[] Families { get; init; } = [];

        [JsonPropertyName("genericVisionPatterns")]
        public string[] GenericVisionPatterns { get; init; } = [];
    }

    internal sealed class FamilyEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("sizeToken")]
        public string SizeToken { get; init; } = "";

        [JsonPropertyName("matchAny")]
        public string[] MatchAny { get; init; } = [];

        [JsonPropertyName("excludeAny")]
        public string[] ExcludeAny { get; init; } = [];

        [JsonPropertyName("mmproj")]
        public MmprojEntry? Mmproj { get; init; }

        [JsonPropertyName("hfSearch")]
        public string[] HfSearch { get; init; } = [];
    }

    internal sealed class MmprojEntry
    {
        [JsonPropertyName("localName")]
        public string LocalName { get; init; } = "";

        [JsonPropertyName("url")]
        public string Url { get; init; } = "";

        [JsonPropertyName("repoId")]
        public string? RepoId { get; init; }

        [JsonPropertyName("remotePrefer")]
        public string[] RemotePrefer { get; init; } = [];
    }

    internal static int GetRegistryVersion(string root) => Load(root).Version;

    internal static bool LooksVisionCapable(string modelFileName, string root)
    {
        if (TryMatchFamily(modelFileName, root) is not null)
            return true;

        var doc = Load(root);
        var upper = modelFileName;
        foreach (var pattern in doc.GenericVisionPatterns)
        {
            if (Regex.IsMatch(upper, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    internal static MatchedFamily? TryMatchFamily(string modelFileName, string root)
    {
        if (string.IsNullOrWhiteSpace(modelFileName))
            return null;

        var doc = Load(root);
        foreach (var family in doc.Families)
        {
            if (!MatchesFamily(modelFileName, family))
                continue;
            return new MatchedFamily(family, family.SizeToken);
        }

        return null;
    }

    internal static string? GetSizeToken(string modelFileName, string root) =>
        TryMatchFamily(modelFileName, root)?.SizeToken;

    internal static MmprojDownloadSpec? GetCanonicalSpec(MatchedFamily matched)
    {
        var mm = matched.Entry.Mmproj;
        if (mm is null || string.IsNullOrWhiteSpace(mm.Url) || string.IsNullOrWhiteSpace(mm.LocalName))
            return null;

        return new MmprojDownloadSpec(mm.Url, mm.LocalName, mm.RepoId, matched.Entry.Id);
    }

    internal static IReadOnlyList<string> GetHfSearchTerms(string modelFileName, string root)
    {
        var terms = new List<string>();
        var matched = TryMatchFamily(modelFileName, root);
        if (matched is not null)
            terms.AddRange(matched.Entry.HfSearch);

        var baseName = Path.GetFileNameWithoutExtension(modelFileName);
        if (!string.IsNullOrWhiteSpace(baseName))
            terms.Add(baseName);

        var stem = NormalizeModelStem(modelFileName);
        if (!string.IsNullOrWhiteSpace(stem) && !terms.Contains(stem, StringComparer.OrdinalIgnoreCase))
            terms.Add(stem);

        if (!string.IsNullOrWhiteSpace(stem) && !stem.EndsWith("-GGUF", StringComparison.OrdinalIgnoreCase))
            terms.Add($"{stem}-GGUF");

        return terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> GetRepoPriorityPrefixes(string root) =>
        Load(root).RepoPriorityPrefixes;

    internal static string[] GetRemotePrefer(MatchedFamily? matched) =>
        matched?.Entry.Mmproj?.RemotePrefer ?? [];

    internal static FileInfo? FindLocalMmproj(string modelFileName, string modelsDir, string root)
    {
        if (!Directory.Exists(modelsDir))
            return null;

        var candidates = Directory.EnumerateFiles(modelsDir, "*.gguf")
            .Where(f => LlamaDefaultModel.IsMmprojFileName(Path.GetFileName(f)))
            .Select(f => new FileInfo(f))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var matched = TryMatchFamily(modelFileName, root);
        if (matched is not null)
        {
            var canonical = matched.Entry.Mmproj?.LocalName;
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                var exact = candidates.FirstOrDefault(c =>
                    string.Equals(c.Name, canonical, StringComparison.OrdinalIgnoreCase));
                if (exact is not null)
                    return exact;
            }

            var byToken = candidates.FirstOrDefault(c =>
                c.Name.Contains(matched.SizeToken, StringComparison.OrdinalIgnoreCase));
            if (byToken is not null)
                return byToken;

            if (string.Equals(matched.SizeToken, "E2B", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var preferred in new[] { LlamaDefaultModel.MmprojFileName, "mmproj-F16.gguf", "mmproj-BF16.gguf" })
                {
                    var hit = candidates.FirstOrDefault(c =>
                        string.Equals(c.Name, preferred, StringComparison.OrdinalIgnoreCase));
                    if (hit is not null)
                        return hit;
                }

                return candidates.FirstOrDefault(c =>
                    !Regex.IsMatch(c.Name, "E4B|26B|12B|Uncensored", RegexOptions.IgnoreCase)
                    && Regex.IsMatch(c.Name, "^mmproj-(F16|BF16)\\.gguf$", RegexOptions.IgnoreCase));
            }
        }

        var stem = NormalizeModelStem(modelFileName);
        if (!string.IsNullOrWhiteSpace(stem))
        {
            var stemHit = candidates.FirstOrDefault(c =>
                c.Name.Contains(stem, StringComparison.OrdinalIgnoreCase)
                || modelFileName.Contains(
                    c.Name.Replace("mmproj-", "", StringComparison.OrdinalIgnoreCase)
                        .Replace(".gguf", "", StringComparison.OrdinalIgnoreCase),
                    StringComparison.OrdinalIgnoreCase));
            if (stemHit is not null)
                return stemHit;
        }

        return null;
    }

    internal static string NormalizeModelStem(string modelFileName)
    {
        var cur = Path.GetFileNameWithoutExtension(modelFileName);
        for (var i = 0; i < 8; i++)
        {
            var next = QuantSuffix.Replace(cur, "");
            if (string.Equals(next, cur, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(next))
                break;
            cur = next;
        }

        return cur;
    }

    private static bool MatchesFamily(string modelFileName, FamilyEntry family)
    {
        foreach (var exclude in family.ExcludeAny)
        {
            if (modelFileName.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var token in family.MatchAny)
        {
            if (modelFileName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static RegistryDocument Load(string root)
    {
        var path = ResolveRegistryPath(root);
        if (!File.Exists(path))
            return new RegistryDocument { Version = 0 };

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RegistryDocument>(json, JsonOptions)
                ?? new RegistryDocument { Version = 0 };
        }
        catch
        {
            return new RegistryDocument { Version = 0 };
        }
    }

    private static string ResolveRegistryPath(string root) =>
        Path.Combine(root, "config", "mmproj-families.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
