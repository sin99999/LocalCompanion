using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Localization;

namespace LocalCompanion.ViewModels;

public sealed partial class RagSourceRowViewModel : ObservableObject
{
    private readonly Action<string, bool>? _onEnabledChanged;

    public RagSourceRowViewModel(
        string source,
        int chunks,
        bool exists,
        bool isEnabled,
        Action<string, bool>? onEnabledChanged = null)
    {
        Source = source;
        Chunks = chunks;
        Exists = exists;
        IsEnabled = isEnabled;
        _onEnabledChanged = onEnabledChanged;
        RefreshLocalization();
    }

    public string Source { get; }

    public int Chunks { get; }

    public bool Exists { get; }

    public string DisplayName => Path.GetFileName(Source);

    public string DeleteLabel => LocalizationService.Instance.Get("Common.Delete");

    public string EnabledLabel => LocalizationService.Instance.Get("Settings.Rag.Source.UseInSearch");

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; } = "";

    partial void OnIsEnabledChanged(bool value) => _onEnabledChanged?.Invoke(Source, value);

    public void RefreshLocalization(LocalizationService? loc = null)
    {
        loc ??= LocalizationService.Instance;
        DetailText = Exists
            ? loc.Format("Settings.Rag.Source.Chunks", Chunks)
            : loc.Format("Settings.Rag.Source.ChunksMissing", Chunks);
        OnPropertyChanged(nameof(EnabledLabel));
        OnPropertyChanged(nameof(DeleteLabel));
    }
}
