using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private bool _suppressRagOptionSave;

    [ObservableProperty]
    public partial bool RagUseHtmlMarkdown { get; set; } = true;

    [ObservableProperty]
    public partial bool RagUseLlmStructurer { get; set; }

    [ObservableProperty]
    public partial bool RagSaveStructurerCache { get; set; } = true;

    [ObservableProperty]
    public partial bool RagUsePdfLayoutReader { get; set; }

    [ObservableProperty]
    public partial string RagIngestReportText { get; set; } = "";

    [ObservableProperty]
    public partial string RagIngestUrl { get; set; } = "";

    public string UiRagHtmlMarkdown { get; private set; } = "";
    public string UiRagHtmlMarkdownHint { get; private set; } = "";
    public string UiRagLlmStructurer { get; private set; } = "";
    public string UiRagLlmStructurerHint { get; private set; } = "";
    public string UiRagSaveCache { get; private set; } = "";
    public string UiRagPdfLayoutReader { get; private set; } = "";
    public string UiRagPdfLayoutReaderHint { get; private set; } = "";
    public string UiRagIngestUrl { get; private set; } = "";
    public string UiRagIngestUrlButton { get; private set; } = "";

    partial void OnRagUseHtmlMarkdownChanged(bool value) => SaveRagIngestOptions();
    partial void OnRagUseLlmStructurerChanged(bool value) => SaveRagIngestOptions();
    partial void OnRagSaveStructurerCacheChanged(bool value) => SaveRagIngestOptions();
    partial void OnRagUsePdfLayoutReaderChanged(bool value) => SaveRagIngestOptions();

    private void LoadRagIngestOptions()
    {
        _suppressRagOptionSave = true;
        var s = _appearance.Current;
        RagUseHtmlMarkdown = s.RagUseHtmlMarkdown;
        RagUseLlmStructurer = s.RagUseLlmStructurer;
        RagSaveStructurerCache = s.RagSaveStructurerCache;
        RagUsePdfLayoutReader = s.RagUsePdfLayoutReader;
        _suppressRagOptionSave = false;
    }

    private void SaveRagIngestOptions()
    {
        if (_suppressRagOptionSave)
            return;
        _appearance.Save(new AppSettingsDto
        {
            ConfirmHistoryDelete = _appearance.Current.ConfirmHistoryDelete,
            ThemeMode = _appearance.Current.ThemeMode,
            ChatFontFamily = _appearance.Current.ChatFontFamily,
            ChatFontSize = _appearance.Current.ChatFontSize,
            UserDisplayName = _appearance.Current.UserDisplayName,
            RagUseHtmlMarkdown = RagUseHtmlMarkdown,
            RagUseLlmStructurer = RagUseLlmStructurer,
            RagSaveStructurerCache = RagSaveStructurerCache,
            RagUsePdfLayoutReader = RagUsePdfLayoutReader,
            MemoryEnabled = _appearance.Current.MemoryEnabled,
            MemoryAutoExtractOnClose = _appearance.Current.MemoryAutoExtractOnClose,
            ChatSearchEnabled = false,
            SpeechInputEnabled = _appearance.Current.SpeechInputEnabled,
        });
    }

    internal void ApplyIngestResult(RagService.RagIngestResult result)
    {
        SetRagStatus("Settings.Rag.IngestDone", result.Files, result.Chunks);
        if (result.Stats is { } stats)
        {
            RagIngestReportText = _loc.Format(
                "Settings.Rag.IngestReport",
                stats.DocKind,
                stats.DefinitionChunks,
                stats.FaqChunks,
                stats.ArticleChunks);
        }
        else
        {
            RagIngestReportText = "";
        }

        if (result.Skipped.Count > 0)
            RagIngestReportText = string.IsNullOrWhiteSpace(RagIngestReportText)
                ? string.Join("\n", result.Skipped)
                : RagIngestReportText + "\n" + string.Join("\n", result.Skipped);
    }

    [RelayCommand]
    private async Task IngestRagUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(RagIngestUrl))
            return;

        IsBusy = true;
        ClearRagError();
        SetRagStatus("Settings.Rag.Ingesting");
        try
        {
            var result = await _rag.IngestUrlAsync(RagIngestUrl.Trim(), ct);
            ApplyIngestResult(result);
            RagIngestUrl = "";
            Refresh();
        }
        catch (Exception ex)
        {
            SetRagError(ex);
            SetRagStatus("Settings.Rag.IngestFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
