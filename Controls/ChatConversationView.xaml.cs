using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using LocalCompanion.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.Controls;

public sealed partial class ChatConversationView : UserControl
{
    public static readonly DependencyProperty MessagesProperty =
        DependencyProperty.Register(
            nameof(Messages),
            typeof(IList),
            typeof(ChatConversationView),
            new PropertyMetadata(null, OnMessagesPropertyChanged));

    private const double ScrollBottomTolerance = 2;

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<ChatLineViewModel> _trackedLines = new();
    private IList? _messages;
    private ScrollViewer? _listScrollHost;
    private bool _scrollScheduled;
    private bool _autoScrollToEnd = true;
    private bool _isProgrammaticScroll;

    public ChatConversationView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        Loaded += OnLoaded;
    }

    public IList? Messages
    {
        get => (IList?)GetValue(MessagesProperty);
        set => SetValue(MessagesProperty, value);
    }

    public void ApplyAppearance(string fontFamily, double fontSize) =>
        ChatMessageBody.SetConversationAppearance(fontFamily, fontSize);

    public void ScrollToEnd()
    {
        if (_messages is null || _messages.Count == 0)
            return;

        if (_listScrollHost is not null)
        {
            _isProgrammaticScroll = true;
            _listScrollHost.ChangeView(null, _listScrollHost.ScrollableHeight, null, disableAnimation: true);
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _isProgrammaticScroll = false;
                _autoScrollToEnd = IsListScrolledToBottom();
            });
            return;
        }

        var last = _messages[_messages.Count - 1];
        MessageList.ScrollIntoView(last, ScrollIntoViewAlignment.Leading);
    }

    private static void OnMessagesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatConversationView view)
            view.AttachMessages(e.OldValue as IList, e.NewValue as IList);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachMessages(_messages, null);
        if (_listScrollHost is not null)
            _listScrollHost.ViewChanged -= OnListScrollViewChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachListScrollHost();
        ScheduleScrollToEnd();
    }

    private void AttachMessages(IList? oldMessages, IList? newMessages)
    {
        if (oldMessages is INotifyCollectionChanged oldNotifier)
            oldNotifier.CollectionChanged -= OnMessagesCollectionChanged;

        foreach (var line in _trackedLines.ToArray())
            UntrackLine(line);

        _messages = newMessages;
        MessageList.ItemsSource = newMessages;

        if (newMessages is INotifyCollectionChanged newNotifier)
            newNotifier.CollectionChanged += OnMessagesCollectionChanged;

        if (newMessages is not null)
        {
            foreach (ChatLineViewModel line in newMessages)
                TrackLine(line);
        }

        ScheduleScrollToEnd();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ChatLineViewModel line in e.NewItems)
                TrackLine(line);
        }

        if (e.OldItems is not null)
        {
            foreach (ChatLineViewModel line in e.OldItems)
                UntrackLine(line);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && _messages is not null)
        {
            foreach (var line in _trackedLines.ToArray())
                UntrackLine(line);
            foreach (ChatLineViewModel line in _messages)
                TrackLine(line);
        }

        ScheduleScrollToEnd();
    }

    private void TrackLine(ChatLineViewModel line)
    {
        if (!_trackedLines.Add(line))
            return;

        line.PropertyChanged += OnLinePropertyChanged;
    }

    private void UntrackLine(ChatLineViewModel line)
    {
        if (!_trackedLines.Remove(line))
            return;

        line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatLineViewModel.Text)
            or nameof(ChatLineViewModel.ReasoningText)
            or nameof(ChatLineViewModel.Header))
        {
            ScheduleScrollToEnd();
        }
    }

    private void AttachListScrollHost()
    {
        if (_listScrollHost is not null)
            _listScrollHost.ViewChanged -= OnListScrollViewChanged;

        MessageList.UpdateLayout();
        _listScrollHost = FindDescendantScrollViewer(MessageList);
        if (_listScrollHost is not null)
            _listScrollHost.ViewChanged += OnListScrollViewChanged;
    }

    private void OnListScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isProgrammaticScroll)
            return;

        _autoScrollToEnd = IsListScrolledToBottom();
    }

    private bool IsListScrolledToBottom()
    {
        if (_listScrollHost is null)
            return true;

        return _listScrollHost.VerticalOffset >= _listScrollHost.ScrollableHeight - ScrollBottomTolerance;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void ScheduleScrollToEnd()
    {
        if (_scrollScheduled || !_autoScrollToEnd)
            return;

        _scrollScheduled = true;
        _dispatcherQueue.TryEnqueue(() =>
        {
            _scrollScheduled = false;
            ScrollToEnd();
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToEnd);
        });
    }
}
