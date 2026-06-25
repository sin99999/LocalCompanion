using System.Text.Json;
using LocalCompanion.Data;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class VoicevoxSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();

    public VoicevoxSettingsStore(RagDatabase db)
    {
        _path = Path.Combine(db.DataDirectory, "voicevox-settings.json");
    }

    public bool SettingsFileExists()
    {
        lock (_lock)
            return File.Exists(_path);
    }

    public void ApplyFirstRunDefaultsIfNeeded()
    {
        if (SettingsFileExists())
            return;

        Save(new VoicevoxSettingsDto
        {
            Enabled = true,
            AutoSpeak = true,
        });
    }

    public VoicevoxSettingsDto Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return new VoicevoxSettingsDto { AutoSpeak = true };

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<VoicevoxSettingsDto>(json, JsonOpts) ?? new VoicevoxSettingsDto();
            }
            catch
            {
                return new VoicevoxSettingsDto();
            }
        }
    }

    public VoicevoxSettingsDto Save(VoicevoxSettingsDto dto)
    {
        var normalized = Normalize(dto);
        lock (_lock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(normalized, JsonOpts));
        }
        return normalized;
    }

    private static VoicevoxSettingsDto Normalize(VoicevoxSettingsDto dto)
    {
        return new VoicevoxSettingsDto
        {
            Enabled = dto.Enabled,
            AutoSpeak = dto.AutoSpeak,
            SpeakInJapanesePronunciation = dto.SpeakInJapanesePronunciation,
            SpeakerId = Math.Max(0, dto.SpeakerId),
            SpeakerChosenByUser = dto.SpeakerChosenByUser,
            SpeedScale = Clamp(dto.SpeedScale, 0.5, 2.0),
            PitchScale = Clamp(dto.PitchScale, -0.15, 0.15),
            IntonationScale = Clamp(dto.IntonationScale, 0.0, 2.0),
            VolumeScale = Clamp(dto.VolumeScale, 0.0, 2.0),
            PrePhonemeLength = Clamp(dto.PrePhonemeLength, 0.0, 1.5),
            PostPhonemeLength = Clamp(dto.PostPhonemeLength, 0.0, 1.5),
        };
    }

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
}
