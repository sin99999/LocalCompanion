using LocalCompanion;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using SelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;

namespace LocalCompanion.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    private CancellationTokenSource? _pageCts;

    public SettingsPage()
    {
        ViewModel = AppServices.Get<SettingsPageViewModel>();
        InitializeComponent();
        ViewModel.BindUiDispatcher(DispatcherQueue);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
        // 初回レイアウト前にタブ数を確定（VOICEVOX 未導入時は 5 本）
        ViewModel.Refresh();
        SyncVoicevoxTab();
        UpdateVoicevoxPoweredByLinkText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SettingsTabs.LayoutUpdated -= OnSettingsTabsLayoutUpdatedForEqualTabs;
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.VoicevoxPoweredByText))
            UpdateVoicevoxPoweredByLinkText();
        if (e.PropertyName == nameof(SettingsPageViewModel.IsVoicevoxInstalled))
            SyncVoicevoxTab();
    }

    private void UpdateVoicevoxPoweredByLinkText()
    {
        VoicevoxPoweredByLinkText.Inlines.Clear();
        var link = new Hyperlink { NavigateUri = new Uri(VoicevoxOptions.OfficialWebsiteUrl) };
        link.Inlines.Add(new Run { Text = ViewModel.VoicevoxPoweredByText });
        VoicevoxPoweredByLinkText.Inlines.Add(link);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
        ViewModel.Refresh();
        SyncVoicevoxTab();
        UpdateVoicevoxPoweredByLinkText();

        // TabView の初回レイアウト後にデータ取得と等幅再計算を行う（OnNavigatedTo で await すると左寄せで固まる）
        if (SettingsTabs.IsLoaded)
            BeginSettingsPageActivation();
    }

    private void OnSettingsTabsLoaded(object sender, RoutedEventArgs e) =>
        BeginSettingsPageActivation();

    private void BeginSettingsPageActivation()
    {
        SyncVoicevoxTab();
        EnsureEqualTabHeaders();
        ScheduleSettingsDataRefresh();
    }

    /// <summary>
    /// TabView の Equal 幅は初回 Measure 時に strip 幅が未確定だと左詰まりのまま固まる。タブ切替で直るのと同じ再計算を初回から行う。
    /// </summary>
    private void EnsureEqualTabHeaders()
    {
        if (SettingsTabs.ActualWidth <= 0)
        {
            SettingsTabs.LayoutUpdated -= OnSettingsTabsLayoutUpdatedForEqualTabs;
            SettingsTabs.LayoutUpdated += OnSettingsTabsLayoutUpdatedForEqualTabs;
            return;
        }

        SettingsTabs.LayoutUpdated -= OnSettingsTabsLayoutUpdatedForEqualTabs;
        SettingsTabs.TabWidthMode = TabViewWidthMode.SizeToContent;
        SettingsTabs.TabWidthMode = TabViewWidthMode.Equal;
    }

    private void OnSettingsTabsLayoutUpdatedForEqualTabs(object? sender, object e) =>
        EnsureEqualTabHeaders();

    private void ScheduleSettingsDataRefresh()
    {
        var token = _pageCts?.Token ?? CancellationToken.None;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                await ViewModel.RefreshRuntimeHealthAsync(token);
                if (ViewModel.IsVoicevoxInstalled)
                    await ViewModel.LoadVoicevoxSpeakersAsync(token);
                UpdateVoicevoxPoweredByLinkText();
                EnsureEqualTabHeaders();
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>
    /// VOICEVOX タブの出し入れのみ。順序は XAML 固定（基本→モデル→キャラ→RAG→記憶→VOICEVOX）。
    /// </summary>
    private void SyncVoicevoxTab()
    {
        var installed = ViewModel.IsVoicevoxInstalled;
        var contains = SettingsTabs.TabItems.Contains(VoicevoxTab);
        var expectedCount = SettingsTabCatalog.VisibleTabCount(installed);

        if (!installed && contains)
        {
            if (ReferenceEquals(SettingsTabs.SelectedItem, VoicevoxTab))
                SettingsTabs.SelectedItem = GeneralTab;

            SettingsTabs.TabItems.Remove(VoicevoxTab);
            EnsureEqualTabHeaders();
        }
        else if (installed && !contains)
        {
            SettingsTabs.TabItems.Add(VoicevoxTab);
            EnsureEqualTabHeaders();
        }

        System.Diagnostics.Debug.Assert(
            SettingsTabs.TabItems.Count == expectedCount,
            $"Settings tabs: expected {expectedCount}, actual {SettingsTabs.TabItems.Count}, voicevox={installed}");
    }

    private void OnSettingsTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args) =>
        args.Cancel = true;

    private async void OnSettingsTabSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.FirstOrDefault() is TabViewItem item
            && ReferenceEquals(item, VoicevoxTab)
            && ViewModel.IsVoicevoxInstalled)
            await ViewModel.LoadVoicevoxSpeakersAsync();

        if (args.AddedItems.FirstOrDefault() is TabViewItem { Header: not null })
            await ViewModel.RefreshRuntimeHealthAsync();
    }

    private async void OnIngestFileClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsSettingsInputEnabled)
            return;
        var path = await RagPathPicker.PickFileAsync(null, App.WindowHandle);
        if (path is null)
            return;
        await ViewModel.IngestPathAsync(path, _pageCts?.Token ?? CancellationToken.None);
    }

    private async void OnIngestFolderClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.IsSettingsInputEnabled)
            return;
        var path = await RagPathPicker.PickFolderAsync(null, App.WindowHandle);
        if (path is null)
            return;
        await ViewModel.IngestPathAsync(path, _pageCts?.Token ?? CancellationToken.None);
    }

    private void OnDeleteRagSourceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.IsSettingsInputEnabled)
            return;
        if (sender is Button { Tag: string source })
            ViewModel.DeleteRagSourceCommand.Execute(source);
    }

    private async void OnBrowseModelsFolderClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.IsSettingsInputEnabled)
            return;
        var initial = ViewModel.HasAdditionalModelsFolder ? ViewModel.AdditionalModelsFolder : null;
        var path = await RagPathPicker.PickModelsFolderAsync(initial, App.WindowHandle);
        if (string.IsNullOrWhiteSpace(path))
            return;
        ViewModel.SetAdditionalModelsFolder(path);
    }

    private void OnClearModelsFolderClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ViewModel.SetAdditionalModelsFolder(null);
}
