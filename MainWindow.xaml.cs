using LocalCompanion.Localization;
using LocalCompanion.Pages;
using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;
using LocalCompanion.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace LocalCompanion;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static int _startupOnce;
    private TaskCompletionSource<bool>? _languageChoiceTcs;
    private TaskCompletionSource<bool>? _firstRunSetupTcs;

    private const double MinNavPaneWidth = 175;
    private const double MaxNavPaneWidth = 400;
    private const double NavChromeWidth = 76;
    private const double NavPaneWidthExtra = 10;
    private const double NavMenuItemHeight = 44;
    private const double NavToggleRowHeight = 52;
    private const double NavTopMenuRows = 3;
    private const double NavFooterMenuRows = 1;
    private const double NavHistoryGuardHeight = 8;
    private const double NavPanePadding = 24;
    private const int ConversationHistoryLimit = 100;

    private bool _isPaneExpanded = true;

    private double _startupTargetPercent;
    private double _startupDisplayedPercent;
    private DispatcherQueueTimer? _startupProgressTimer;

    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        ScrollViewer.SetHorizontalScrollBarVisibility(ThreadList, ScrollBarVisibility.Disabled);

        try
        {
            var iconPath = Path.Combine(AppPaths.Current.ContentRoot, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch
        {
            /* タスクバーアイコンは任意 */
        }

        ResizeForCompanionUi();
        ConfigureTitleBar();
        AppWindow.Changed += OnAppWindowChanged;
        Instance = this;
        AppServices.Get<AppAppearanceService>().Changed += OnAppAppearanceChanged;

        LocalizationService.Instance.Changed += OnLocalizationChanged;
        ApplyLocalization();

        StartupStatusText.Text = LocalizationService.Instance.Get("Splash.Wait");
        Activated += OnFirstActivated;
        AppWindow.Closing += (_, _) => { /* shutdown は Closed で一度だけ */ };
        Closed += (_, _) =>
        {
            AppWindow.Changed -= OnAppWindowChanged;
            LocalizationService.Instance.Changed -= OnLocalizationChanged;
            AppServices.Get<AppAppearanceService>().Changed -= OnAppAppearanceChanged;
            _ = FinalizeChatSessionOnCloseAsync();
            Instance = null;
            CompanionStartup.ShutdownInBackground();
        };
    }

    private void OnLocalizationChanged(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void OnAppAppearanceChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(ApplyRuntimeAppearance);
    }

    private void ApplyRuntimeAppearance()
    {
        AppServices.Get<AppAppearanceService>().ApplyRuntimeTheme();
        if (ContentFrame.Content is ChatPage chatPage)
            chatPage.ApplyAppearanceFromSettings();
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            return;
        if (Interlocked.Exchange(ref _startupOnce, 1) != 0)
            return;
        Activated -= OnFirstActivated;

        try
        {
            if (LocalizationService.Instance.NeedsLanguageChoice)
            {
                LanguagePickerPanel.Visibility = Visibility.Visible;
                FirstRunSetupPanel.Visibility = Visibility.Collapsed;
                StartupProgressPanel.Visibility = Visibility.Collapsed;
                await WaitForLanguageChoiceAsync();
            }

            var paths = AppPaths.Current;
            var dataDir = AppPaths.ResolveUserDataDirectory(
                LlamaInstallConfig.Load(paths.Root).DataDirectory);
            if (FirstRunModelSetup.NeedsSetup(dataDir, paths.ModelsDirectory))
            {
                LanguagePickerPanel.Visibility = Visibility.Collapsed;
                FirstRunSetupPanel.Visibility = Visibility.Visible;
                StartupProgressPanel.Visibility = Visibility.Collapsed;
                ApplyFirstRunSetupLocalization();
                await WaitForFirstRunSetupAsync();
            }

            FirstRunSetupPanel.Visibility = Visibility.Collapsed;
            StartupProgressPanel.Visibility = Visibility.Visible;
            await CompanionStartup.RunAsync(report =>
            {
                _ = DispatcherQueue.TryEnqueue(() => ApplyStartupProgress(report));
            });

            await WaitForStartupProgressCatchUpAsync(100, TimeSpan.FromMilliseconds(750));
            StopStartupProgressAnimator();
            SplashPanel.Visibility = Visibility.Collapsed;
            NavView.IsPaneOpen = true;
            SetConversationHistoryPanelVisible(_isPaneExpanded);
            UpdatePaneToggleLayout();
            NavView.SelectedItem = ChatNavItem;
            ReloadCharacterChoices();
            ContentFrame.Navigate(typeof(ChatPage));
            ReloadConversationThreads();
            UpdateConversationHistoryLayout();
            _ = OfferVoicevoxUpdateIfNeededAsync();
        }
        catch (Exception ex)
        {
            StartupStatusText.Text = UserFacingErrorLocalizer.Localize(ex);
        }
    }

    private Task WaitForLanguageChoiceAsync()
    {
        _languageChoiceTcs = new TaskCompletionSource<bool>();
        return _languageChoiceTcs.Task;
    }

    private void OnPickJapaneseClick(object sender, RoutedEventArgs e) =>
        CompleteLanguageChoice(AppLanguage.Japanese);

    private void OnPickEnglishClick(object sender, RoutedEventArgs e) =>
        CompleteLanguageChoice(AppLanguage.English);

    private void CompleteLanguageChoice(AppLanguage language)
    {
        LocalizationService.Instance.SetLanguage(language);
        LanguagePickerPanel.Visibility = Visibility.Collapsed;
        ApplyLocalization();
        _languageChoiceTcs?.TrySetResult(true);
    }

    private Task WaitForFirstRunSetupAsync()
    {
        _firstRunSetupTcs = new TaskCompletionSource<bool>();
        return _firstRunSetupTcs.Task;
    }

    private void OnFirstRunContinueClick(object sender, RoutedEventArgs e)
    {
        var dataDir = AppPaths.ResolveUserDataDirectory(
            LlamaInstallConfig.Load(AppPaths.Current.Root).DataDirectory);
        FirstRunModelSetup.CompleteDefaultSetup(dataDir);
        CompleteFirstRunSetup();
    }

    private async void OnFirstRunChooseFolderClick(object sender, RoutedEventArgs e)
    {
        FirstRunSetupErrorText.Visibility = Visibility.Collapsed;
        var path = await RagPathPicker.PickModelsFolderAsync(null, App.WindowHandle);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var paths = AppPaths.Current;
        var dataDir = AppPaths.ResolveUserDataDirectory(
            LlamaInstallConfig.Load(paths.Root).DataDirectory);
        if (!FirstRunModelSetup.TryCompleteExternalFolder(paths.ModelsDirectory, dataDir, path, out var errorKey))
        {
            var loc = LocalizationService.Instance;
            FirstRunSetupErrorText.Text = loc.Get(errorKey ?? "FirstRun.Setup.Error.InvalidFolder");
            FirstRunSetupErrorText.Visibility = Visibility.Visible;
            return;
        }

        CompleteFirstRunSetup();
    }

    private void CompleteFirstRunSetup()
    {
        FirstRunSetupPanel.Visibility = Visibility.Collapsed;
        StartupProgressPanel.Visibility = Visibility.Visible;
        FirstRunSetupErrorText.Visibility = Visibility.Collapsed;
        FirstRunSetupFolderText.Visibility = Visibility.Collapsed;
        ApplyLocalization();
        _firstRunSetupTcs?.TrySetResult(true);
    }

    private void ApplyFirstRunSetupLocalization()
    {
        var loc = LocalizationService.Instance;
        FirstRunSetupTitleText.Text = loc.Get("FirstRun.Setup.Title");
        FirstRunSetupDescriptionText.Text = loc.Get("FirstRun.Setup.Description");
        FirstRunSetupOwnModelHintText.Text = loc.Get("FirstRun.Setup.OwnModelHint");
        FirstRunContinueButton.Content = loc.Get("FirstRun.Setup.Continue");
        FirstRunChooseFolderButton.Content = loc.Get("FirstRun.Setup.ChooseFolder");
    }

    private void ApplyStartupProgress(StartupProgressReport report)
    {
        StartupStatusText.Text = report.Message;

        if (report.Percent is double pct)
        {
            _startupTargetPercent = Math.Clamp(pct, 0, 100);
            StartupProgressBar.IsIndeterminate = false;
            StartupPercentText.Visibility = Visibility.Visible;
            EnsureStartupProgressAnimator();
        }
        else
        {
            StopStartupProgressAnimator();
            StartupProgressBar.IsIndeterminate = true;
            StartupPercentText.Visibility = Visibility.Collapsed;
        }
    }

    private void EnsureStartupProgressAnimator()
    {
        if (_startupProgressTimer is not null)
            return;

        _startupProgressTimer = DispatcherQueue.CreateTimer();
        _startupProgressTimer.Interval = TimeSpan.FromMilliseconds(40);
        _startupProgressTimer.Tick += OnStartupProgressTimerTick;
        _startupProgressTimer.Start();
        OnStartupProgressTimerTick(_startupProgressTimer, null!);
    }

    private void OnStartupProgressTimerTick(DispatcherQueueTimer sender, object args)
    {
        var delta = _startupTargetPercent - _startupDisplayedPercent;
        if (delta < 0)
        {
            // フェーズ切替などで目標が下がったときは逆走アニメーションせず即時リセット
            _startupDisplayedPercent = _startupTargetPercent;
        }
        else if (delta < 0.4)
        {
            _startupDisplayedPercent = _startupTargetPercent;
        }
        else
        {
            var step = Math.Max(0.9, delta * 0.16);
            _startupDisplayedPercent = Math.Min(_startupDisplayedPercent + step, _startupTargetPercent);
        }

        StartupProgressBar.Value = _startupDisplayedPercent;
        StartupPercentText.Text = $"{_startupDisplayedPercent:F0}%";
    }

    private void StopStartupProgressAnimator()
    {
        if (_startupProgressTimer is null)
            return;

        _startupProgressTimer.Tick -= OnStartupProgressTimerTick;
        _startupProgressTimer.Stop();
        _startupProgressTimer = null;
    }

    private async Task WaitForStartupProgressCatchUpAsync(double targetPercent, TimeSpan maxWait)
    {
        _startupTargetPercent = Math.Clamp(targetPercent, 0, 100);
        EnsureStartupProgressAnimator();

        var deadline = DateTime.UtcNow + maxWait;
        while (DateTime.UtcNow < deadline && _startupDisplayedPercent < targetPercent - 0.5)
            await Task.Delay(40).ConfigureAwait(true);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        if (tag == "character")
        {
            ShowCharacterMenu(item);
            RestoreCurrentNavigationSelection();
            return;
        }

        if (tag == "language")
        {
            ShowLanguageMenu(item);
            RestoreCurrentNavigationSelection();
            return;
        }

        var page = tag switch
        {
            "chat" => typeof(ChatPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    public void ReloadConversationThreads()
    {
        var chat = AppServices.Get<ChatService>();
        ThreadList.ItemsSource = chat.ListThreadPreviews(ConversationHistoryLimit);
        ThreadList.SelectedItem = null;
        UpdateConversationHistoryLayout();
    }

    public void EnsureConversationHistoryVisible()
    {
        _isPaneExpanded = true;
        NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        NavView.IsPaneOpen = true;
        SetConversationHistoryPanelVisible(true);
        ApplySidebarPaneWidth();
        UpdatePaneToggleLayout();
        ReloadConversationThreads();
    }

    public void ReloadCharacterChoices()
    {
        var characters = AppServices.Get<CharacterPresetService>();
        var list = characters.List();
        var active = list.ActiveFileName ?? CharacterPresetService.NoneSelection;
        var activePreset = list.Presets.FirstOrDefault(p =>
            string.Equals(p.FileName, active, StringComparison.OrdinalIgnoreCase));
        var loc = LocalizationService.Instance;
        CharacterNavItem.Content = activePreset?.Name ?? loc.Get("Nav.CharacterDefault");
        ApplySidebarPaneWidth();
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        ChatNavItem.Content = loc.Get("Nav.Chat");
        SettingsNavItem.Content = loc.Get("Nav.Settings");
        ConversationHistoryHeader.Text = loc.Get("Nav.ConversationHistory");
        SplashSubtitleText.Text = loc.Get("Splash.Subtitle");
        if (FirstRunSetupPanel.Visibility == Visibility.Visible)
            ApplyFirstRunSetupLocalization();
        if (SplashPanel.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(StartupStatusText.Text))
            StartupStatusText.Text = loc.Get("Splash.Wait");
        LanguageNavItem.Content = loc.Get("Nav.Language");
        ToolTipService.SetToolTip(PaneToggleButton, loc.Get("Nav.TogglePane"));
        ReloadCharacterChoices();
        ApplySidebarPaneWidth();
        StartupProgress.RefreshForLanguage();
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "language" })
        {
            ShowLanguageMenu(args.InvokedItemContainer);
            RestoreCurrentNavigationSelection();
        }
    }

    private void UpdatePaneToggleLayout()
    {
        PaneToggleButton.HorizontalAlignment = _isPaneExpanded
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        // コンパクト時だけアイコン列に合わせて 2px 左へ（MenuItems 本体は触らない）
        PaneToggleButton.Margin = _isPaneExpanded
            ? new Thickness(0)
            : new Thickness(-2, 0, 2, 0);
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        _isPaneExpanded = !_isPaneExpanded;
        NavView.IsPaneOpen = true;
        NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        ApplySidebarPaneWidth();
        if (_isPaneExpanded)
            UpdateConversationHistoryLayout();

        SetConversationHistoryPanelVisible(_isPaneExpanded);
        UpdatePaneToggleLayout();
    }

    private bool IsSidebarCompact => !_isPaneExpanded;

    private double GetNavTopChromeHeight() =>
        NavToggleRowHeight + NavMenuItemHeight * NavTopMenuRows;

    private void ShowLanguageMenu(FrameworkElement target)
    {
        var loc = LocalizationService.Instance;
        var flyout = new MenuFlyout();
        foreach (var choice in new[]
        {
            new LanguageChoiceViewModel(AppLanguage.Japanese, loc.Get("Settings.Language.Option.Ja")),
            new LanguageChoiceViewModel(AppLanguage.English, loc.Get("Settings.Language.Option.En")),
        })
        {
            var item = new MenuFlyoutItem
            {
                Text = choice.DisplayName,
                Tag = choice,
            };
            if (choice.Language == loc.Current)
                item.Icon = new SymbolIcon(Symbol.Accept);
            item.Click += OnLanguageMenuItemClick;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(target, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
        });
    }

    private void OnLanguageMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: LanguageChoiceViewModel choice })
            return;

        LocalizationService.Instance.SetLanguage(choice.Language);
    }

    private void ShowCharacterMenu(FrameworkElement target)
    {
        var characters = AppServices.Get<CharacterPresetService>();
        var list = characters.List();
        var choices = new List<CharacterChoiceViewModel>
        {
            new(CharacterPresetService.NoneSelection, LocalizationService.Instance.Get("Character.Default")),
        };
        choices.AddRange(list.Presets.Select(p => new CharacterChoiceViewModel(p.FileName, p.Name)));

        var flyout = new MenuFlyout();
        foreach (var choice in choices)
        {
            var item = new MenuFlyoutItem { Text = choice.DisplayName, Tag = choice };
            item.Click += OnCharacterMenuItemClick;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(target, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });
    }

    private void OnCharacterMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CharacterChoiceViewModel choice })
            return;

        SelectCharacter(choice);
    }

    private void SelectCharacter(CharacterChoiceViewModel choice)
    {
        if (ContentFrame.Content is ChatPage activeChat && activeChat.ViewModel.IsBusy)
        {
            activeChat.ViewModel.NotifyBusyMutationBlocked();
            return;
        }

        var characters = AppServices.Get<CharacterPresetService>();
        try
        {
            if (string.IsNullOrEmpty(choice.FileName) || choice.FileName == CharacterPresetService.NoneSelection)
                characters.SelectNone();
            else
                characters.Select(choice.FileName);

            if (ContentFrame.Content is ChatPage chatPage)
            {
                chatPage.ViewModel.ReloadCharacterChoices();
                chatPage.ViewModel.BeginNewConversation();
            }

            ReloadCharacterChoices();
        }
        catch
        {
            ReloadCharacterChoices();
        }
    }

    private void RestoreCurrentNavigationSelection()
    {
        var target = ContentFrame.CurrentSourcePageType switch
        {
            Type t when t == typeof(SettingsPage) => SettingsNavItem,
            _ => ChatNavItem,
        };
        if (!ReferenceEquals(NavView.SelectedItem, target))
            NavView.SelectedItem = target;
    }

    private void OnThreadSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ThreadList.SelectedItem is not ConversationThreadPreview thread)
            return;

        if (ContentFrame.Content is ChatPage busyPage && busyPage.ViewModel.IsBusy)
        {
            busyPage.ViewModel.NotifyBusyMutationBlocked();
            ThreadList.SelectedItem = null;
            return;
        }

        if (ContentFrame.CurrentSourcePageType != typeof(ChatPage))
        {
            NavView.SelectedItem = ChatNavItem;
            ContentFrame.Navigate(typeof(ChatPage));
        }

        if (ContentFrame.Content is ChatPage chatPage)
        {
            chatPage.ViewModel.LoadConversationSession(thread.SessionId);
            ReloadCharacterChoices();
        }

        ThreadList.SelectedItem = null;
    }

    private static async Task FinalizeChatSessionOnCloseAsync()
    {
        try
        {
            await AppServices.Get<ChatPageViewModel>().FinalizeActiveSessionOnCloseAsync();
        }
        catch
        {
            /* ignore */
        }
    }

    private void OnNavPaneOpened(NavigationView sender, object args)
    {
        if (_isPaneExpanded)
            SetConversationHistoryPanelVisible(true);
        UpdateConversationHistoryLayout();
    }

    private void OnNavPaneClosed(NavigationView sender, object args)
    {
        if (!_isPaneExpanded)
            return;

        SetConversationHistoryPanelVisible(false);
    }

    private void OnNavViewSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateConversationHistoryLayout();

    private void SetConversationHistoryPanelVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        ConversationHistoryHost.Visibility = v;
        ConversationHistoryPanel.Visibility = v;
    }

    private void UpdateConversationHistoryLayout()
    {
        if (NavView.ActualHeight <= 0 || IsSidebarCompact)
            return;

        var headerHeight = ConversationHistoryHeader.ActualHeight > 0
            ? ConversationHistoryHeader.ActualHeight
            : 20;
        var footerMenuHeight = NavMenuItemHeight * NavFooterMenuRows;
        var topChrome = GetNavTopChromeHeight();
        var historyChrome = NavHistoryGuardHeight + headerHeight + NavPanePadding;
        var footerMax = NavView.ActualHeight - topChrome - footerMenuHeight - NavPanePadding;
        if (footerMax < historyChrome + 72)
            footerMax = historyChrome + 72;

        ConversationHistoryPanel.MinHeight = footerMax;
        ConversationHistoryPanel.MaxHeight = footerMax;
        var listHeight = Math.Max(72, footerMax - NavHistoryGuardHeight - headerHeight);
        ThreadList.MaxHeight = listHeight;
        ThreadList.MinHeight = listHeight;
    }

    private void ResizeForCompanionUi()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = (int)(1120 * scale);
        AppWindow.Resize(new SizeInt32(width, (int)(800 * scale)));
        ApplySidebarPaneWidth();
    }

    private void ConfigureTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        var titleBar = AppWindow.TitleBar;
        var background = Color.FromArgb(255, 14, 14, 14);
        var foreground = Colors.White;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = Color.FromArgb(255, 220, 220, 220);
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 200, 200, 200);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 44, 44, 44);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 60, 60, 60);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
            _ = DispatcherQueue.TryEnqueue(ReapplySidebarAfterWindowResize);
    }

    private void ReapplySidebarAfterWindowResize()
    {
        ApplySidebarPaneWidth();
        if (_isPaneExpanded)
            UpdateConversationHistoryLayout();
    }

    private void ApplySidebarPaneWidth()
    {
        if (!_isPaneExpanded)
        {
            NavView.OpenPaneLength = NavView.CompactPaneLength;
            return;
        }

        UpdateNavigationPaneWidth();
    }

    private void UpdateNavigationPaneWidth()
    {
        // 会話履歴タイトルは幅計算に含めない（リスト側で省略表示）
        var labels = new List<string>
        {
            ChatNavItem.Content?.ToString() ?? "",
            SettingsNavItem.Content?.ToString() ?? "",
            CharacterNavItem.Content?.ToString() ?? "",
            LanguageNavItem.Content?.ToString() ?? "",
        };

        var maxText = labels.Max(MeasureNavTextWidth);
        var desired = Math.Clamp(
            maxText + NavChromeWidth + NavPaneWidthExtra,
            MinNavPaneWidth,
            MaxNavPaneWidth);

        var windowWidth = AppWindow.Size.Width;
        var cap = Math.Min(MaxNavPaneWidth, Math.Max(MinNavPaneWidth, windowWidth * 0.42));
        NavView.OpenPaneLength = Math.Min(desired, cap);
    }

    private static double MeasureNavTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var block = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            TextWrapping = TextWrapping.NoWrap,
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }

    private async Task OfferVoicevoxUpdateIfNeededAsync()
    {
        try
        {
            var coordinator = AppServices.Get<VoicevoxStartupCoordinator>();
            await coordinator.CheckAndOfferUpdateOnStartupAsync(Content.XamlRoot);
        }
        catch
        {
            /* 更新確認失敗は起動を妨げない */
        }
    }
}
