using LocalCompanion;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace LocalCompanion.Pages;

public sealed partial class ChatPage : Page
{
    public ChatPageViewModel ViewModel { get; }

    private MenuFlyoutItem? _insertImageItem;
    private MenuFlyout? _inputContextFlyout;
    private AppAppearanceService? _appearance;
    private Brush? _inputAreaDefaultBorderBrush;
    private Thickness _inputAreaDefaultBorderThickness;

    public ChatPage()
    {
        ViewModel = AppServices.Get<ChatPageViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        ScheduleScrollToEnd();

    private void OnConversationThreadsChanged(object? sender, EventArgs e) =>
        MainWindow.Instance?.ReloadConversationThreads();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ReloadCharacterChoices();
        _ = ViewModel.RefreshHealthAsync();
        MainWindow.Instance?.ReloadConversationThreads();
        ApplyAppearanceFromSettings();
        ScheduleScrollToEnd();
        ScheduleFocusInput();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPageViewModel.IsBusy) or nameof(ChatPageViewModel.SendStopButtonLabel))
            DispatcherQueue.TryEnqueue(SyncSendStopButton);

        if (e.PropertyName == nameof(ChatPageViewModel.StatusText))
            ScheduleScrollToEnd();

        if (e.PropertyName == nameof(ChatPageViewModel.IsBusy) && !ViewModel.IsBusy)
            ScheduleFocusInput();
    }

    private void SyncSendStopButton()
    {
        var label = ViewModel.SendStopButtonLabel;
        if (!Equals(SendStopButton.Content, label))
            SendStopButton.Content = label;
    }

    private void ScheduleFocusInput()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            InputBox.Focus(FocusState.Programmatic);
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.BindUiDispatcher(DispatcherQueue);
        _appearance = AppServices.Get<AppAppearanceService>();
        _appearance.Changed += OnAppearanceChanged;
        ApplyAppearanceFromSettings();
        CacheInputAreaBorderDefaults();
        WireInputFlyouts();
        WireInputContextFlyout();
        LocalizationService.Instance.Changed += OnLocalizationChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        ViewModel.ConversationThreadsChanged += OnConversationThreadsChanged;
        SyncSendStopButton();
        ScheduleScrollToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        ViewModel.ConversationThreadsChanged -= OnConversationThreadsChanged;
        if (_appearance is not null)
            _appearance.Changed -= OnAppearanceChanged;
        LocalizationService.Instance.Changed -= OnLocalizationChanged;
        InsertMenuButton.Flyout = null;
        ChatOptionsButton.Flyout = null;
        _insertImageItem = null;
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        WireInputFlyouts();
        WireInputContextFlyout();
        SyncSendStopButton();
    }

    private void WireInputFlyouts()
    {
        if (InsertMenuButton.Flyout is MenuFlyout oldInsert)
            oldInsert.Opening -= OnInsertFlyoutOpening;
        if (ChatOptionsButton.Flyout is MenuFlyout oldOptions)
            oldOptions.Opening -= OnOptionsFlyoutOpening;

        var insertFlyout = new MenuFlyout();
        _insertImageItem = new MenuFlyoutItem
        {
            Text = ViewModel.UiInsertImage,
            Icon = new SymbolIcon(Symbol.Pictures),
        };
        _insertImageItem.Click += async (_, _) => await PickImageAttachmentAsync();
        insertFlyout.Items.Add(_insertImageItem);

        var textItem = new MenuFlyoutItem
        {
            Text = ViewModel.UiInsertText,
            Icon = new SymbolIcon(Symbol.Document),
        };
        textItem.Click += async (_, _) => await PickTextAttachmentAsync();
        insertFlyout.Items.Add(textItem);

        var urlItem = new MenuFlyoutItem
        {
            Text = ViewModel.UiInsertUrl,
            Icon = new SymbolIcon(Symbol.Globe),
        };
        urlItem.Click += async (_, _) => await PickUrlAttachmentAsync();
        insertFlyout.Items.Add(urlItem);
        insertFlyout.Opening += OnInsertFlyoutOpening;
        InsertMenuButton.Flyout = insertFlyout;

        var optionsFlyout = new MenuFlyout();
        optionsFlyout.Items.Add(CreateToggleOption(ViewModel.UiRag, () => ViewModel.UseRag, v => ViewModel.UseRag = v));
        optionsFlyout.Items.Add(CreateToggleOption(ViewModel.UiHistory, () => ViewModel.UseHistory, v => ViewModel.UseHistory = v));
        optionsFlyout.Items.Add(CreateToggleOption(ViewModel.UiReasoning, () => ViewModel.UseReasoning, v => ViewModel.UseReasoning = v));
        optionsFlyout.Items.Add(new MenuFlyoutSeparator());
        optionsFlyout.Items.Add(CreateActionOption(ViewModel.UiClearHistory, "\uE74D", () => _ = ConfirmAndClearHistoryAsync()));
        optionsFlyout.Opening += OnOptionsFlyoutOpening;
        ChatOptionsButton.Flyout = optionsFlyout;

        ApplyInsertImageItemState();
    }

    private void WireInputContextFlyout()
    {
        _inputContextFlyout ??= new MenuFlyout();
        _inputContextFlyout.Opening -= OnInputContextFlyoutOpening;
        _inputContextFlyout.Opening += OnInputContextFlyoutOpening;
        InputBox.ContextFlyout = _inputContextFlyout;
        InputBox.RightTapped -= OnInputBoxRightTapped;
        InputBox.RightTapped += OnInputBoxRightTapped;
    }

    private void OnInputBoxRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        InputBox.Focus(FocusState.Pointer);
        if (_inputContextFlyout is null)
            return;

        OnInputContextFlyoutOpening(_inputContextFlyout, EventArgs.Empty);
        _inputContextFlyout.ShowAt(InputBox, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
            Position = e.GetPosition(InputBox),
        });
        e.Handled = true;
    }

    private void OnInputContextFlyoutOpening(object? sender, object e)
    {
        if (_inputContextFlyout is null)
            return;

        var loc = LocalizationService.Instance;
        _inputContextFlyout.Items.Clear();

        var paste = new MenuFlyoutItem
        {
            Text = loc.Get("Common.Paste"),
            KeyboardAcceleratorTextOverride = "Ctrl+V",
        };
        paste.Click += async (_, _) => await PasteIntoInputAsync();
        paste.IsEnabled = Clipboard.GetContent().Contains(StandardDataFormats.Text);
        _inputContextFlyout.Items.Add(paste);

        var cut = new MenuFlyoutItem
        {
            Text = loc.Get("Common.Cut"),
            KeyboardAcceleratorTextOverride = "Ctrl+X",
        };
        cut.Click += (_, _) => CutInputSelection();
        cut.IsEnabled = InputBox.SelectionLength > 0;
        _inputContextFlyout.Items.Add(cut);

        var copy = new MenuFlyoutItem
        {
            Text = loc.Get("Common.Copy"),
            KeyboardAcceleratorTextOverride = "Ctrl+C",
        };
        copy.Click += (_, _) => CopyInputSelection();
        copy.IsEnabled = InputBox.SelectionLength > 0;
        _inputContextFlyout.Items.Add(copy);

        _inputContextFlyout.Items.Add(new MenuFlyoutSeparator());

        var selectAll = new MenuFlyoutItem
        {
            Text = loc.Get("Common.SelectAll"),
            KeyboardAcceleratorTextOverride = "Ctrl+A",
        };
        selectAll.Click += (_, _) => InputBox.SelectAll();
        selectAll.IsEnabled = InputBox.Text.Length > 0;
        _inputContextFlyout.Items.Add(selectAll);
    }

    private async Task PasteIntoInputAsync()
    {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.Text))
            return;

        var text = await data.GetTextAsync();
        if (string.IsNullOrEmpty(text))
            return;

        ReplaceInputSelection(text);
    }

    private void CutInputSelection()
    {
        if (InputBox.SelectionLength == 0)
            return;

        CopyInputSelection();
        ReplaceInputSelection(string.Empty);
    }

    private void CopyInputSelection()
    {
        if (InputBox.SelectionLength == 0)
            return;

        var package = new DataPackage();
        package.SetText(InputBox.SelectedText);
        Clipboard.SetContent(package);
    }

    private void ReplaceInputSelection(string replacement)
    {
        var start = Math.Clamp(InputBox.SelectionStart, 0, InputBox.Text.Length);
        var len = Math.Clamp(InputBox.SelectionLength, 0, InputBox.Text.Length - start);
        var text = InputBox.Text;
        InputBox.Text = text[..start] + replacement + text[(start + len)..];
        InputBox.SelectionStart = start + replacement.Length;
        InputBox.SelectionLength = 0;
    }

    private async void OnInsertFlyoutOpening(object? sender, object e)
    {
        await ViewModel.RefreshHealthAsync();
        ApplyInsertImageItemState();
    }

    private async void OnOptionsFlyoutOpening(object? sender, object e)
    {
        await ViewModel.RefreshHealthAsync();
        ApplyInsertImageItemState();
    }

    private void ApplyInsertImageItemState()
    {
        if (_insertImageItem is null)
            return;

        var enabled = ViewModel.ImageAttachEnabled;
        _insertImageItem.IsEnabled = enabled;
        ToolTipService.SetToolTip(_insertImageItem, enabled ? null : ViewModel.ImageAttachHint);
        if (!enabled)
            ViewModel.ClearPendingImageAttachments();
    }

    private const double OptionsFlyoutIconSize = 16;

    private MenuFlyoutItem CreateToggleOption(
        string label,
        Func<bool> getter,
        Action<bool> setter)
    {
        var item = new MenuFlyoutItem { Text = label };
        void RefreshIcon() => item.Icon = CreateFlyoutLeadingIcon("\uE73E", getter());

        item.Loaded += (_, _) => RefreshIcon();
        item.Click += (_, _) =>
        {
            setter(!getter());
            RefreshIcon();
        };
        return item;
    }

    private MenuFlyoutItem CreateActionOption(string label, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = label,
            Icon = CreateFlyoutLeadingIcon(glyph, visible: true),
        };
        item.Click += (_, _) => action();
        return item;
    }

    private FontIcon CreateFlyoutLeadingIcon(string glyph, bool visible)
    {
        return new FontIcon
        {
            Glyph = glyph,
            FontSize = OptionsFlyoutIconSize,
            Width = OptionsFlyoutIconSize,
            Height = OptionsFlyoutIconSize,
            Opacity = visible ? 1 : 0,
        };
    }

    private async Task PickImageAttachmentAsync()
    {
        try
        {
            var file = await WinUiStoragePickerService.PickImageFileAsync();
            if (file is null)
                return;

            await ViewModel.AddImageAttachmentAsync(file);
        }
        catch (Exception ex)
        {
            ViewModel.ReportError(ex);
        }
    }

    private void CacheInputAreaBorderDefaults()
    {
        _inputAreaDefaultBorderBrush = InputAreaBorder.BorderBrush;
        _inputAreaDefaultBorderThickness = InputAreaBorder.BorderThickness;
    }

    private void SetInputAreaDropHighlight(bool active)
    {
        if (_inputAreaDefaultBorderBrush is null)
            CacheInputAreaBorderDefaults();

        if (active)
        {
            var accent = TryFindThemeBrush("AccentFillColorDefaultBrush")
                ?? TryFindThemeBrush("SystemAccentColorDark1Brush")
                ?? TryFindThemeBrush("SystemControlHighlightAccentBrush");
            if (accent is not null)
            {
                InputAreaBorder.BorderBrush = accent;
                InputAreaBorder.BorderThickness = new Thickness(2);
            }

            return;
        }

        InputAreaBorder.BorderBrush = _inputAreaDefaultBorderBrush!;
        InputAreaBorder.BorderThickness = _inputAreaDefaultBorderThickness;
    }

    private static Brush? TryFindThemeBrush(string key)
    {
        var resources = Application.Current.Resources;
        return resources.ContainsKey(key) && resources[key] is Brush brush ? brush : null;
    }

    private void OnInputAreaDragOver(object sender, DragEventArgs e)
    {
        try
        {
            e.AcceptedOperation = DataPackageOperation.None;
            if (!ViewModel.ImageAttachEnabled || !ViewModel.IsInputEnabled)
            {
                SetInputAreaDropHighlight(false);
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                SetInputAreaDropHighlight(false);
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            SetInputAreaDropHighlight(true);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "InputAreaDragOver");
            SetInputAreaDropHighlight(false);
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnInputAreaDragLeave(object sender, DragEventArgs e)
    {
        try
        {
            SetInputAreaDropHighlight(false);
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "InputAreaDragLeave");
        }
    }

    private async void OnInputAreaDrop(object sender, DragEventArgs e)
    {
        SetInputAreaDropHighlight(false);
        e.Handled = true;

        if (!ViewModel.ImageAttachEnabled || !ViewModel.IsInputEnabled)
            return;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            StorageFile? imageFile = null;
            var hasUnsupported = false;

            foreach (var item in items)
            {
                if (item is not StorageFile file)
                    continue;

                if (ChatAttachmentFileRules.IsSupportedImagePath(file.Path))
                {
                    imageFile = file;
                    break;
                }

                hasUnsupported = true;
            }

            if (imageFile is not null)
            {
                await ViewModel.AddImageAttachmentAsync(imageFile);
                return;
            }

            if (hasUnsupported)
            {
                ViewModel.ReportError(new InvalidOperationException(
                    LocalizationService.Instance.Get("Chat.Attachment.UnsupportedImageFormat")));
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportError(ex);
            StartupLog.Write(ex, "InputAreaDrop");
        }
    }

    private async Task PickTextAttachmentAsync()
    {
        try
        {
            var file = await WinUiStoragePickerService.PickTextFileAsync();
            if (file is null)
                return;

            await ViewModel.AddTextAttachmentAsync(file);
        }
        catch (Exception ex)
        {
            ViewModel.ReportError(ex);
        }
    }

    private async Task PickUrlAttachmentAsync()
    {
        var loc = LocalizationService.Instance;
        var urlBox = new TextBox
        {
            PlaceholderText = loc.Get("Chat.Url.Dialog.Placeholder"),
            TextWrapping = TextWrapping.NoWrap,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = loc.Get("Chat.Url.Dialog.Title"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = loc.Get("Chat.Url.Dialog.Message"),
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    urlBox,
                },
            },
            PrimaryButtonText = loc.Get("Chat.Url.Dialog.Load"),
            SecondaryButtonText = loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Secondary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var url = urlBox.Text.Trim();
        if (url.Length == 0)
            return;

        var previousStatus = ViewModel.StatusText;
        ViewModel.StatusText = loc.Get("Chat.Url.Loading");
        try
        {
            await ViewModel.AddUrlAttachmentAsync(url);
        }
        catch (Exception ex)
        {
            ViewModel.ReportError(ex);
        }
        finally
        {
            ViewModel.StatusText = previousStatus;
        }
    }

    private void OnRemoveAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatAttachmentItemViewModel item })
            return;

        if (ViewModel.RemoveAttachmentCommand.CanExecute(item))
            ViewModel.RemoveAttachmentCommand.Execute(item);
    }

    private void ScheduleScrollToEnd()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConversationView.ScrollToEnd();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                ConversationView.ScrollToEnd());
        });
    }

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
            return;

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (!ctrl.HasFlag(CoreVirtualKeyStates.Down))
            return;

        if (e.Key != VirtualKey.C)
            return;

        e.Handled = true;
        if (ViewModel.StopGenerationCommand.CanExecute(null))
            ViewModel.StopGenerationCommand.Execute(null);
    }

    private void OnSendOrStopClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            if (ViewModel.StopGenerationCommand.CanExecute(null))
                ViewModel.StopGenerationCommand.Execute(null);
            return;
        }

        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }

    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up)
        {
            e.Handled = ViewModel.RecallPreviousInput();
            MoveInputCaretToEnd();
            return;
        }

        if (e.Key == VirtualKey.Down)
        {
            e.Handled = ViewModel.RecallNextInput();
            MoveInputCaretToEnd();
            return;
        }

        if (e.Key != VirtualKey.Enter)
            return;

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shift.HasFlag(CoreVirtualKeyStates.Down))
            return;

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }

    private void MoveInputCaretToEnd()
    {
        InputBox.SelectionStart = InputBox.Text.Length;
        InputBox.SelectionLength = 0;
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearanceFromSettings();

    public void ApplyAppearanceFromSettings()
    {
        var appearance = _appearance ?? AppServices.Get<AppAppearanceService>();
        var settings = appearance.Current;
        ConversationView.ApplyAppearance(settings.ChatFontFamily, settings.ChatFontSize);
        InputBox.FontFamily = new FontFamily(settings.ChatFontFamily);
        InputBox.FontSize = settings.ChatFontSize;
        ViewModel.RefreshUserMessageHeaders();
    }

    private async Task ConfirmAndClearHistoryAsync()
    {
        if (ViewModel.IsBusy)
        {
            ViewModel.NotifyBusyMutationBlocked();
            return;
        }

        var appearance = _appearance ?? AppServices.Get<AppAppearanceService>();
        if (appearance.ShouldConfirmHistoryDelete())
        {
            var loc = LocalizationService.Instance;
            var checkBox = new CheckBox { Content = loc.Get("Chat.ClearHistory.Confirm.DontAskAgain") };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = loc.Get("Chat.ClearHistory.Confirm.Title"),
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = loc.Get("Chat.ClearHistory.Confirm.Message"),
                            TextWrapping = TextWrapping.WrapWholeWords,
                        },
                        checkBox,
                    },
                },
                PrimaryButtonText = loc.Get("Common.Delete"),
                SecondaryButtonText = loc.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Secondary,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            if (checkBox.IsChecked == true)
                appearance.SetConfirmHistoryDelete(false);
        }

        if (ViewModel.ClearHistoryCommand.CanExecute(null))
            ViewModel.ClearHistoryCommand.Execute(null);
    }
}
