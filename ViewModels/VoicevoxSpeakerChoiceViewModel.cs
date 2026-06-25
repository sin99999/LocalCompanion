using LocalCompanion.Localization;

namespace LocalCompanion.ViewModels;

public sealed record VoicevoxSpeakerChoiceViewModel(int Id, string SpeakerName, string StyleName)
{
    public string DisplayName =>
        VoicevoxSpeakerLocalizer.FormatDisplayName(Id, SpeakerName, StyleName);
}
