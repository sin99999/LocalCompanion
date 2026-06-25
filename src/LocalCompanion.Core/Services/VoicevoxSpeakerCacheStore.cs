using System.Text.Json;
using LocalCompanion.Data;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>公式 API から取得した話者一覧のスナップショット（更新後の差分検出用）。</summary>
public sealed class VoicevoxSpeakerCacheStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();

    public VoicevoxSpeakerCacheStore(RagDatabase db)
    {
        _path = Path.Combine(db.DataDirectory, "voicevox-speakers-cache.json");
    }

    public IReadOnlyList<VoicevoxSpeakerCacheEntry> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return Array.Empty<VoicevoxSpeakerCacheEntry>();

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<VoicevoxSpeakerCacheEntry>>(json, JsonOpts)
                    ?? new List<VoicevoxSpeakerCacheEntry>();
            }
            catch
            {
                return Array.Empty<VoicevoxSpeakerCacheEntry>();
            }
        }
    }

    public void Save(IReadOnlyList<VoicevoxSpeakerStyleDto> speakers)
    {
        var entries = speakers
            .Select(s => new VoicevoxSpeakerCacheEntry(s.Id, s.SpeakerName, s.StyleName))
            .ToList();
        lock (_lock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOpts));
        }
    }

    public IReadOnlyList<VoicevoxSpeakerCacheEntry> FindNewSpeakers(IReadOnlyList<VoicevoxSpeakerStyleDto> speakers)
    {
        var known = Load().Select(s => s.Id).ToHashSet();
        if (known.Count == 0)
            return Array.Empty<VoicevoxSpeakerCacheEntry>();

        return speakers
            .Where(s => !known.Contains(s.Id))
            .Select(s => new VoicevoxSpeakerCacheEntry(s.Id, s.SpeakerName, s.StyleName))
            .ToList();
    }
}
