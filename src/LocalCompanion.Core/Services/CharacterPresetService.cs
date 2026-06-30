using System.Text.Json;
using System.Text.Json.Serialization;
using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed record CharacterPresetInfo(string FileName, string Name);

public sealed record CharacterPresetsResponse(
    string CharactersDirectory,
    string? ActiveFileName,
    IReadOnlyList<CharacterPresetInfo> Presets,
    CharacterSamplingLimitsDto Sampling);

public sealed class CharacterPresetService
{
    /// <summary>チャットで「選択なし＝デフォルト」を選んだときの activeFileName。</summary>
    public const string NoneSelection = "";

    /// <summary>既定 AI の会話セッション用 DB キー（左ペインの履歴には出さない）。</summary>
    public const string DefaultAiPresetKey = "__default_ai__";

    public static string ResolveSessionPresetKey(string? activeFileName) =>
        IsNoneSelection(activeFileName) ? DefaultAiPresetKey : activeFileName!;

    public static bool IsDefaultAiSession(string? presetKey) =>
        string.Equals(presetKey, DefaultAiPresetKey, StringComparison.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RagDatabase _db;
    private readonly LlamaOptions _opt;
    private readonly string _charactersDir;
    private readonly string _selectionPath;

    public CharacterPresetService(AppPaths paths, RagDatabase db, IOptions<LlamaOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
        _charactersDir = ResolveCharactersDirectory(paths, _opt);
        Directory.CreateDirectory(_charactersDir);
        _selectionPath = Path.Combine(_charactersDir, "selection.json");
        EnsureInitialSelection();
    }

    private static string ResolveCharactersDirectory(AppPaths paths, LlamaOptions opt)
    {
        if (!string.IsNullOrWhiteSpace(opt.CharactersDirectory))
            return Path.GetFullPath(opt.CharactersDirectory.Trim());

        var candidates = new[]
        {
            Path.Combine(AppPaths.GetInstallDirectory(), "characters"),
            Path.Combine(paths.Root, "characters"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidates)
        {
            if (!seen.Add(dir))
                continue;
            if (CountPresetJsonFiles(dir) > 0)
                return Path.GetFullPath(dir);
        }

        return Path.GetFullPath(Path.Combine(paths.Root, "characters"));
    }

    private static int CountPresetJsonFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        return Directory.EnumerateFiles(directory, "*.json")
            .Count(f => !string.Equals(Path.GetFileName(f), "selection.json", StringComparison.OrdinalIgnoreCase));
    }

    public string CharactersDirectory => _charactersDir;

    public CharacterPresetsResponse List()
    {
        var presets = ScanPresetFiles();
        var active = LoadActiveFileName();
        return new CharacterPresetsResponse(_charactersDir, active, presets, CharacterSamplingLimits.ToDto());
    }

    /// <summary>チャットで選択中のキャラ json ファイル名。選択なし（デフォルト）時は <see cref="NoneSelection"/>。</summary>
    public string? GetActivePresetFileName() => LoadActiveFileName();

    public CharacterProfileDto GetActive()
    {
        var active = LoadActiveFileName();
        if (IsNoneSelection(active))
        {
            var bare = BareModelProfile();
            WriteServerSyncFile(bare);
            return bare;
        }

        if (!string.IsNullOrWhiteSpace(active))
        {
            var loaded = TryLoadFile(active!);
            if (loaded is not null)
            {
                WriteServerSyncFile(loaded);
                return loaded;
            }
        }

        var bareFallback = BareModelProfile();
        WriteServerSyncFile(bareFallback);
        return bareFallback;
    }

    public static bool IsNoneSelection(string? fileName) =>
        string.IsNullOrEmpty(fileName);

    public CharacterProfileDto BareModelProfile() =>
        new(
            Name: "AI",
            Persona: "",
            SpeakingStyle: "",
            Temperature: _opt.Temperature,
            TopP: _opt.TopP,
            TopK: _opt.TopK,
            ContextLength: _opt.ContextLength,
            MaxOutputTokens: _opt.MaxOutputTokens);

    public CharacterProfileDto? GetByFileName(string fileName)
    {
        var safe = SanitizeExistingFileName(fileName);
        return TryLoadFile(safe);
    }

    /// <summary>characters/*.json に保存。activate=false ならチャットの選択は変えない（設定画面用）。</summary>
    public string Save(CharacterProfileDto profile, bool activate = false)
    {
        profile = CharacterSamplingLimits.Normalize(profile);
        var fileName = ToFileName(profile.Name);
        var path = Path.Combine(_charactersDir, fileName);
        var json = JsonSerializer.Serialize(profile, JsonOpts);
        AtomicFile.WriteAllText(path, json);
        if (activate)
        {
            SetActiveFileName(fileName);
            WriteServerSyncFile(profile);
        }
        else if (string.Equals(LoadActiveFileName(), fileName, StringComparison.OrdinalIgnoreCase))
        {
            // 選択中キャラの上書き保存はサーバー連携ファイルにも反映する
            WriteServerSyncFile(profile);
        }
        return fileName;
    }

    public CharacterProfileDto Select(string? fileName)
    {
        if (IsNoneSelection(fileName))
            return SelectNone();

        var safe = SanitizeExistingFileName(fileName!);
        var path = Path.Combine(_charactersDir, safe);
        if (!File.Exists(path))
            throw new LocalizedServiceException("Settings.Character.Error.NotFound", safe);

        var profile = TryLoadFile(safe) ?? throw new LocalizedServiceException("Settings.Character.Error.LoadFailed", safe);
        SetActiveFileName(safe);
        WriteServerSyncFile(profile);
        return profile;
    }

    public CharacterProfileDto SelectNone()
    {
        SetActiveFileName(null);
        var bare = BareModelProfile();
        WriteServerSyncFile(bare);
        return bare;
    }

    public CharacterProfileDto? GetByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        return TryLoadFile(ToFileName(displayName));
    }

    public void Delete(string fileName)
    {
        var safe = SanitizeExistingFileName(fileName);
        var path = Path.Combine(_charactersDir, safe);
        if (!File.Exists(path))
            throw new LocalizedServiceException("Settings.Character.Error.NotFound", safe);

        File.Delete(path);
        var active = LoadActiveFileName();
        if (!string.Equals(active, safe, StringComparison.OrdinalIgnoreCase))
            return;

        var remaining = ScanPresetFiles();
        if (remaining.Count > 0)
        {
            Select(remaining[0].FileName);
            return;
        }

        SelectNone();
    }

    private List<CharacterPresetInfo> ScanPresetFiles()
    {
        var list = new List<CharacterPresetInfo>();
        foreach (var path in Directory.EnumerateFiles(_charactersDir, "*.json"))
        {
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, "selection.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var profile = TryLoadFile(fileName);
            var display = profile?.Name ?? Path.GetFileNameWithoutExtension(fileName);
            list.Add(new CharacterPresetInfo(fileName, display));
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private CharacterProfileDto? TryLoadFile(string fileName)
    {
        var path = Path.Combine(_charactersDir, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var p = JsonSerializer.Deserialize<CharacterProfileDto>(json, JsonOpts);
            if (p is null) return null;
            if (!json.Contains("topK", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("top_k", StringComparison.OrdinalIgnoreCase))
            {
                p = p with { TopK = CharacterDefaults.AppTopK };
            }
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                p = p with { Name = Path.GetFileNameWithoutExtension(fileName) };
            }
            return CharacterSamplingLimits.Normalize(p);
        }
        catch
        {
            return null;
        }
    }

    private string? LoadActiveFileName()
    {
        if (!File.Exists(_selectionPath)) return null;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(_selectionPath));
            if (doc.RootElement.TryGetProperty("activeFileName", out var a))
                return a.GetString();
            if (doc.RootElement.TryGetProperty("ActiveFileName", out var b))
                return b.GetString();
        }
        catch { }
        return null;
    }

    private void SetActiveFileName(string? fileName)
    {
        var json = JsonSerializer.Serialize(
            new { activeFileName = IsNoneSelection(fileName) ? null : fileName },
            JsonOpts);
        AtomicFile.WriteAllText(_selectionPath, json);
    }

    internal static string ToFileName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "character";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var baseName = new string(chars).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "character";
        return baseName + ".json";
    }

    private string SanitizeExistingFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new LocalizedServiceException("Settings.Character.Error.InvalidFileName");
        if (string.Equals(name, "selection.json", StringComparison.OrdinalIgnoreCase))
            throw new LocalizedServiceException("Settings.Character.Error.CannotDeleteSelection");
        return name;
    }

    private void EnsureInitialSelection()
    {
        // 初回起動時は必ず「選択なし（デフォルト）」にする
        if (!File.Exists(_selectionPath))
            SelectNone();
    }

    private void WriteServerSyncFile(CharacterProfileDto p)
    {
        try
        {
            var path = Path.Combine(_db.DataDirectory, "character-settings.json");
            var json = JsonSerializer.Serialize(new
            {
                contextLength = p.ContextLength,
                temperature = p.Temperature,
                maxOutputTokens = p.MaxOutputTokens
            });
            AtomicFile.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // 書けないとサーバー起動時に古い設定が使われ続けるため、原因をログに残す
            StartupLog.Write(ex, "WriteServerSyncFile");
        }
    }
}
