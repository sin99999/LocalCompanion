using LocalCompanion;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
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
        UpdateVoicevoxPoweredByLinkText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.VoicevoxPoweredByText))
            UpdateVoicevoxPoweredByLinkText();
    }

    private void UpdateVoicevoxPoweredByLinkText()
    {
        VoicevoxPoweredByLinkText.Inlines.Clear();
        var link = new Hyperlink { NavigateUri = new Uri(VoicevoxOptions.OfficialWebsiteUrl) };
        link.Inlines.Add(new Run { Text = ViewModel.VoicevoxPoweredByText });
        VoicevoxPoweredByLinkText.Inlines.Add(link);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
        ViewModel.Refresh();
        await ViewModel.RefreshRuntimeHealthAsync();
        SyncVoicevoxTab();
        if (ViewModel.IsVoicevoxInstalled)
            await ViewModel.LoadVoicevoxSpeakersAsync();
        UpdateVoicevoxPoweredByLinkText();
    }

    private void SyncVoicevoxTab()
    {
        var installed = ViewModel.IsVoicevoxInstalled;
        var contains = SettingsTabs.TabItems.Contains(VoicevoxTab);

        if (!installed && contains)
        {
            if (ReferenceEquals(SettingsTabs.SelectedItem, VoicevoxTab))
                SettingsTabs.SelectedItem = GeneralTab;

            SettingsTabs.TabItems.Remove(VoicevoxTab);
        }
        else if (installed && !contains)
        {
            SettingsTabs.TabItems.Add(VoicevoxTab);
        }

        ResetSettingsTabOrder();
    }

    private void ResetSettingsTabOrder()
    {
        var selected = SettingsTabs.SelectedItem as TabViewItem;
        var desired = BuildDesiredTabs();
        if (NeedsTabOrderReset(desired))
        {
            SettingsTabs.TabItems.Clear();
            foreach (var tab in desired)
                SettingsTabs.TabItems.Add(tab);
        }

        if (selected is not null && SettingsTabs.TabItems.Contains(selected))
            SettingsTabs.SelectedItem = selected;
        else if (SettingsTabs.TabItems.Count > 0)
            SettingsTabs.SelectedItem = SettingsTabs.TabItems[0];

        SettingsTabs.UpdateLayout();
        ResetTabStripVisualStates();
    }

    private List<TabViewItem> BuildDesiredTabs()
    {
        var tabs = new List<TabViewItem> { GeneralTab, ModelTab, CharacterTab, RagTab };
        if (ViewModel.IsVoicevoxInstalled)
            tabs.Add(VoicevoxTab);
        return tabs;
    }

    private bool NeedsTabOrderReset(IReadOnlyList<TabViewItem> desired)
    {
        if (SettingsTabs.TabItems.Count != desired.Count)
            return true;

        for (var i = 0; i < desired.Count; i++)
        {
            if (!ReferenceEquals(SettingsTabs.TabItems[i], desired[i]))
                return true;
        }

        return false;
    }

    private void OnSettingsTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args) =>
        args.Cancel = true;

    private void OnSettingsTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args) =>
        ResetTabStripVisualStates();

    private void OnSettingsTabsPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ResetTabStripVisualStates();

    private void ResetTabStripVisualStates()
    {
        var listView = FindDescendant<ListView>(SettingsTabs, "TabListView");
        if (listView is null)
            return;

        var selected = SettingsTabs.SelectedItem;
        foreach (var item in listView.Items)
        {
            if (listView.ContainerFromItem(item) is not ListViewItem container)
                continue;

            VisualStateManager.GoToState(container, "Normal", true);
            if (ReferenceEquals(item, selected))
                VisualStateManager.GoToState(container, "Selected", true);
        }

        SettingsTabs.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
                return match;

            var found = FindDescendant<T>(child, name);
            if (found is not null)
                return found;
        }

        return null;
    }

    private async void OnSettingsTabSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.FirstOrDefault() is TabViewItem item
            && ReferenceEquals(item, VoicevoxTab)
            && ViewModel.IsVoicevoxInstalled)
            await ViewModel.LoadVoicevoxSpeakersAsync();

        if (args.AddedItems.FirstOrDefault() is TabViewItem { Header: not null })
            await ViewModel.RefreshRuntimeHealthAsync();

        ResetTabStripVisualStates();
    }

    private async void OnIngestFileClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
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
