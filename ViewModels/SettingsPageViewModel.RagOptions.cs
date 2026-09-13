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

    /// <summary>概念グラフの状況（概念・関係・束の数。失敗があればその理由）。</summary>
    [ObservableProperty]
    public partial string RagGraphStatusText { get; set; } = "";

    public string UiRagGraphHint { get; private set; } = "";
    public string UiRagGraphRebuild { get; private set; } = "";

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
        var dto = _appearance.Current.Clone();
        dto.RagUseHtmlMarkdown = RagUseHtmlMarkdown;
        dto.RagUseLlmStructurer = RagUseLlmStructurer;
        dto.RagSaveStructurerCache = RagSaveStructurerCache;
        dto.RagUsePdfLayoutReader = RagUsePdfLayoutReader;
        _appearance.Save(dto);
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

    private void ApplyRagGraphLocalizedUi()
    {
        UiRagGraphHint = _loc.Get("Settings.Rag.Graph.Hint");
        UiRagGraphRebuild = _loc.Get("Settings.Rag.Graph.Rebuild");
        OnPropertyChanged(nameof(UiRagGraphHint));
        OnPropertyChanged(nameof(UiRagGraphRebuild));
        RefreshRagGraphStatus();
    }

    private void RefreshRagGraphStatus()
    {
        try
        {
            var stats = _rag.GetGraphStats();
            var text = _loc.Format("Settings.Rag.Graph.Status", stats.ConceptCount, stats.EdgeCount, stats.CommunityCount);
            var lastError = _rag.GetGraphLastError();
            if (!string.IsNullOrWhiteSpace(lastError))
                text += "\n" + _loc.Format("Settings.Rag.Graph.LastError", lastError);
            RagGraphStatusText = text;
        }
        catch
        {
            RagGraphStatusText = "";
        }
    }

    [RelayCommand]
    private async Task RebuildRagGraphAsync(CancellationToken ct)
    {
        IsBusy = true;
        ClearRagError();
        SetRagStatus("Settings.Rag.Graph.Rebuilding");
        try
        {
            await _rag.RebuildGraphAsync(ct);
            SetRagStatus("Settings.Rag.Graph.Rebuilt");
        }
        catch (Exception ex)
        {
            SetRagError(ex);
            SetRagStatus("Settings.Rag.Graph.RebuildFailed");
        }
        finally
        {
            RefreshRagGraphStatus();
            IsBusy = false;
        }
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
