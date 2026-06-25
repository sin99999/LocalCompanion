namespace LocalCompanion.Models;

public sealed record IngestTextRequest(string Text, string? Source);

public sealed record IngestFileRequest(string Path);

public sealed record DeleteRagSourceRequest(string Source);

public sealed record PickPathRequest(string? Mode, string? InitialPath);

public sealed record VoicevoxTextRequest(string? Text);

public sealed record VoicevoxSynthesizeRequest(
    string? Text,
    bool AutoSpeak = false,
    int? SpeakerId = null,
    double? SpeedScale = null,
    double? PitchScale = null,
    double? IntonationScale = null,
    double? VolumeScale = null);
