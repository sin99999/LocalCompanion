using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LocalCompanion.ViewModels;

public partial class SettingsPageViewModel
{
    private DispatcherQueue? _uiDispatcher;
    private DateTime _modelLoadDeadlineUtc;

    [ObservableProperty]
    public partial string RuntimeHealthText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsModelLoadInProgress { get; set; }

    [ObservableProperty]
    public partial double ModelLoadProgress { get; set; }

    [ObservableProperty]
    public partial string ModelLoadStatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial string ModelLoadRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsMmprojDownloadInProgress { get; set; }

    [ObservableProperty]
    public partial double MmprojDownloadProgress { get; set; }

    [ObservableProperty]
    public partial string MmprojDownloadStatusMessage { get; set; } = "";

    public Visibility ModelLoadProgressVisibility =>
        IsModelLoadInProgress ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MmprojDownloadProgressVisibility =>
        IsMmprojDownloadInProgress ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsModelLoadInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(ModelLoadProgressVisibility));
        OnPropertyChanged(nameof(IsSettingsInputEnabled));
    }

    partial void OnIsMmprojDownloadInProgressChanged(bool value) =>
        OnPropertyChanged(nameof(MmprojDownloadProgressVisibility));

    private CancellationTokenSource? _mmprojDownloadCts;
    private Task? _mmprojDownloadTask;
    private int _mmprojDownloadGeneration;

    public void BindUiDispatcher(DispatcherQueue dispatcher) => _uiDispatcher = dispatcher;

    public async Task RefreshRuntimeHealthAsync(CancellationToken ct = default)
    {
        if (IsModelLoadInProgress)
            return;

        try
        {
            var summary = await _health.GetAsync(ct);
            RunOnUi(() => RuntimeHealthText = summary.Message);
        }
        catch (Exception ex)
        {
            RunOnUi(() => RuntimeHealthText = UserFacingErrorLocalizer.Localize(ex));
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher is null || _uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }

    private static int EstimateModelLoadWaitSeconds(AppPaths paths)
    {
        var settings = LlamaInstallConfig.Load(paths.Root);
        var context = LlamaContextPolicy.CapForServer(settings.ContextLength);
        return Math.Clamp(context / 64, 180, 600);
    }

    private void BeginModelLoadUi(int maxWaitSeconds)
    {
        _modelLoadDeadlineUtc = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        IsModelLoadInProgress = true;
        ModelLoadProgress = 0;
        ModelLoadStatusMessage = LocalizationService.Instance.Get("Settings.Model.Switching");
        UpdateModelLoadRemainingText();
    }

    private void EndModelLoadUi()
    {
        StartupProgress.Handler = null;
        IsModelLoadInProgress = false;
        ModelLoadProgress = 0;
        ModelLoadStatusMessage = "";
        ModelLoadRemainingText = "";
    }

    private void BeginMmprojDownloadUi(string fileName)
    {
        IsMmprojDownloadInProgress = true;
        MmprojDownloadProgress = 0;
        MmprojDownloadStatusMessage = LocalizationService.Instance.Format("Settings.Model.MmprojSearching.Named", fileName);
    }

    private void EndMmprojDownloadUi()
    {
        if (!IsMmprojDownloadInProgress)
            return;

        if (StartupProgress.Handler is not null && !IsModelLoadInProgress)
            StartupProgress.Handler = null;

        IsMmprojDownloadInProgress = false;
        MmprojDownloadProgress = 0;
        MmprojDownloadStatusMessage = "";
    }

    private void QueueMmprojEnsureForSelection(string? modelFileName, string? modelFullPath = null)
    {
        _mmprojDownloadCts?.Cancel();
        _mmprojDownloadCts?.Dispose();
        _mmprojDownloadCts = new CancellationTokenSource();
        var ct = _mmprojDownloadCts.Token;
        var generation = ++_mmprojDownloadGeneration;

        if (string.IsNullOrWhiteSpace(modelFileName))
        {
            EndMmprojDownloadUi();
            return;
        }

        _mmprojDownloadTask = EnsureMmprojForModelAsync(modelFileName, modelFullPath, ct, generation);
    }

    private async Task EnsureMmprojForModelAsync(
        string modelFileName,
        string? modelFullPath,
        CancellationToken ct,
        int generation)
    {
        if (!MmprojSupport.NeedsKnownMmprojDownload(_paths.Root, modelFileName, modelFullPath))
        {
            RunOnUi(() =>
            {
                if (generation == _mmprojDownloadGeneration)
                    UpdateMmprojStatus(modelFileName);
            });
            return;
        }

        RunOnUi(() =>
        {
            if (generation != _mmprojDownloadGeneration)
                return;
            BeginMmprojDownloadUi(modelFileName);
        });

        StartupProgress.Handler = report => RunOnUi(() =>
        {
            if (generation != _mmprojDownloadGeneration)
                return;
            MmprojDownloadStatusMessage = report.Message;
            if (report.Percent is not null)
                MmprojDownloadProgress = report.Percent.Value;
        });

        try
        {
            await MmprojSupport.EnsureKnownMmprojAsync(_paths.Root, modelFileName, modelFullPath, ct);
        }
        catch (OperationCanceledException)
        {
            // 別モデル選択などでキャンセル
        }
        finally
        {
            RunOnUi(() =>
            {
                if (generation != _mmprojDownloadGeneration)
                    return;
                EndMmprojDownloadUi();
                UpdateMmprojStatus(modelFileName);
            });
        }
    }

    private Task WaitForMmprojDownloadAsync()
    {
        var task = _mmprojDownloadTask;
        return task is null || task.IsCompleted ? Task.CompletedTask : task;
    }

    public void RefreshMmprojDownloadForLanguage()
    {
        if (!IsMmprojDownloadInProgress)
            return;

        if (SelectedChatModel is not null && MmprojDownloadProgress <= 0)
        {
            MmprojDownloadStatusMessage = LocalizationService.Instance.Format(
                "Settings.Model.MmprojSearching.Named",
                SelectedChatModel.FileName);
        }

        StartupProgress.RefreshForLanguage();
    }

    private void UpdateModelLoadRemainingText()
    {
        var remaining = Math.Max(0, (int)Math.Ceiling((_modelLoadDeadlineUtc - DateTime.UtcNow).TotalSeconds));
        ModelLoadRemainingText = LocalizationService.Instance.Format("Settings.Model.LoadRemaining", remaining);
    }

    private async Task<int> RestartManagedWithProgressAsync(CancellationToken ct)
    {
        var maxWaitSeconds = EstimateModelLoadWaitSeconds(_paths);
        BeginModelLoadUi(maxWaitSeconds);

        StartupProgress.Handler = report => RunOnUi(() =>
        {
            ModelLoadStatusMessage = report.Message;
            if (report.Percent is not null)
                ModelLoadProgress = report.Percent.Value;
            UpdateModelLoadRemainingText();
        });

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var loadTask = Task.Run(() =>
        {
            _lifecycle.ForceStopAll();
            return LlamaServerNativeHost.EnsureAndStart(_paths);
        }, ct);

        try
        {
            while (!loadTask.IsCompleted)
            {
                RunOnUi(() => UpdateModelLoadRemainingText());
                if (!await timer.WaitForNextTickAsync(ct))
                    break;
            }

            return await loadTask;
        }
        finally
        {
            EndModelLoadUi();
        }
    }
}
