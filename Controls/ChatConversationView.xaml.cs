using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using LocalCompanion.Models;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    private static readonly TimeSpan RebuildThrottle = TimeSpan.FromMilliseconds(120);
    private const double ScrollBottomTolerance = 2;

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<ChatLineViewModel> _trackedLines = new();
    private DispatcherQueueTimer? _rebuildTimer;
    private IList? _messages;
    private ScrollViewer? _documentScrollHost;
    private bool _scrollScheduled;
    private bool _isPointerSelecting;
    private bool _autoScrollToEnd = true;
    private bool _isProgrammaticScroll;
    private string _bodyFontFamily = "Segoe UI";
    private double _bodyFontSize = 14;
    private string _documentText = string.Empty;
    private IReadOnlyList<ChatTextRange> _reasoningRanges = Array.Empty<ChatTextRange>();
    private bool _documentHasRichFormatting;

    public ChatConversationView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        Loaded += OnLoaded;
        ConversationDocument.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnDocumentPointerPressed), true);
        ConversationDocument.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnDocumentPointerReleased), true);
    }

    public IList? Messages
    {
        get => (IList?)GetValue(MessagesProperty);
        set => SetValue(MessagesProperty, value);
    }

    public void ApplyAppearance(string fontFamily, double fontSize)
    {
        _bodyFontFamily = fontFamily;
        _bodyFontSize = fontSize;
        ApplyDocumentAppearance();
    }

    public void ScrollToEnd()
    {
        if (_isPointerSelecting || string.IsNullOrEmpty(_documentText))
            return;

        if (_documentScrollHost is not null)
        {
            _isProgrammaticScroll = true;
            _documentScrollHost.ChangeView(null, _documentScrollHost.ScrollableHeight, null, disableAnimation: true);
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _isProgrammaticScroll = false;
                _autoScrollToEnd = IsDocumentScrolledToBottom();
            });
            return;
        }

        ConversationDocument.Document.Selection.SetRange(int.MaxValue, int.MaxValue);
        ConversationDocument.Document.Selection.ScrollIntoView(PointOptions.None);
    }

    private static void OnMessagesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatConversationView view)
            view.AttachMessages(e.OldValue as IList, e.NewValue as IList);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopRebuildTimer();
        if (_documentScrollHost is not null)
            _documentScrollHost.ViewChanged -= OnDocumentScrollViewChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDocumentAppearance();
        AttachDocumentScrollHost();
    }

    private void AttachMessages(IList? oldMessages, IList? newMessages)
    {
        if (oldMessages is INotifyCollectionChanged oldNotifier)
            oldNotifier.CollectionChanged -= OnMessagesCollectionChanged;

        foreach (var line in _trackedLines.ToArray())
            UntrackLine(line);

        _messages = newMessages;

        if (newMessages is INotifyCollectionChanged newNotifier)
            newNotifier.CollectionChanged += OnMessagesCollectionChanged;

        if (newMessages is not null)
        {
            foreach (ChatLineViewModel line in newMessages)
                TrackLine(line);
        }

        RebuildNow();
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

        ScheduleRebuild();
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
            ScheduleRebuild();
            ScheduleScrollToEnd();
        }
    }

    private void ScheduleRebuild()
    {
        _rebuildTimer ??= _dispatcherQueue.CreateTimer();
        _rebuildTimer.Interval = RebuildThrottle;
        _rebuildTimer.IsRepeating = false;
        if (_rebuildTimer.IsRunning)
            return;

        _rebuildTimer.Tick -= OnRebuildTimerTick;
        _rebuildTimer.Tick += OnRebuildTimerTick;
        _rebuildTimer.Start();
    }

    private void OnRebuildTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= OnRebuildTimerTick;
        sender.Stop();
        RebuildNow();
    }

    private void StopRebuildTimer()
    {
        if (_rebuildTimer is null)
            return;

        _rebuildTimer.Tick -= OnRebuildTimerTick;
        _rebuildTimer.Stop();
    }

    private void RebuildNow()
    {
        if (_isPointerSelecting)
        {
            ScheduleRebuild();
            return;
        }

        var displayText = BuildDocumentText(_messages);
        SyncDocumentText(displayText.Text, displayText.ReasoningRanges);
        ScheduleScrollToEnd();
    }

    private void SyncDocumentText(string newText, IReadOnlyList<ChatTextRange> reasoningRanges)
    {
        if (string.Equals(_documentText, newText, StringComparison.Ordinal))
            return;

        if (CanAppendPlainText(newText, reasoningRanges))
        {
            AppendDocumentText(newText[_documentText.Length..]);
        }
        else if (reasoningRanges.Count > 0)
        {
            UpdateReadOnlyDocument(() =>
                ConversationDocument.Document.SetText(TextSetOptions.FormatRtf, BuildRtf(newText, reasoningRanges)));
            _documentHasRichFormatting = true;
        }
        else if (_documentText.Length > 0
            && newText.Length >= _documentText.Length
            && newText.StartsWith(_documentText, StringComparison.Ordinal))
        {
            AppendDocumentText(newText[_documentText.Length..]);
            _documentHasRichFormatting = false;
        }
        else
        {
            UpdateReadOnlyDocument(() =>
                ConversationDocument.Document.SetText(TextSetOptions.None, newText));
            _documentHasRichFormatting = false;
        }

        _documentText = newText;
        _reasoningRanges = reasoningRanges.ToArray();
    }

    private bool CanAppendPlainText(string newText, IReadOnlyList<ChatTextRange> reasoningRanges)
    {
        if (_documentText.Length == 0
            || newText.Length < _documentText.Length
            || !newText.StartsWith(_documentText, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_documentHasRichFormatting)
            return reasoningRanges.Count == 0;

        return RangesEqual(_reasoningRanges, reasoningRanges)
            && _documentText.Length > LastRangeEnd(reasoningRanges);
    }

    private static bool RangesEqual(IReadOnlyList<ChatTextRange> first, IReadOnlyList<ChatTextRange> second)
    {
        if (first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (first[i] != second[i])
                return false;
        }

        return true;
    }

    private static int LastRangeEnd(IReadOnlyList<ChatTextRange> ranges)
    {
        var end = 0;
        foreach (var range in ranges)
            end = Math.Max(end, range.Start + range.Length);

        return end;
    }

    private void AppendDocumentText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        UpdateReadOnlyDocument(() =>
        {
            ConversationDocument.Document.Selection.SetRange(int.MaxValue, int.MaxValue);
            ConversationDocument.Document.Selection.SetText(TextSetOptions.None, text);
        });
    }

    private static string BuildRtf(string text, IReadOnlyList<ChatTextRange> reasoningRanges)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\uc1{\colortbl ;\red255\green255\blue255;\red155\green155\blue155;\red42\green42\blue42;}\cf1 ");

        var index = 0;
        foreach (var range in reasoningRanges.OrderBy(r => r.Start))
        {
            var start = Math.Clamp(range.Start, 0, text.Length);
            var end = Math.Clamp(range.Start + range.Length, start, text.Length);
            AppendRtfEscaped(sb, text.AsSpan(index, start - index));
            sb.Append(@"\cf2\highlight3 ");
            AppendRtfEscaped(sb, text.AsSpan(start, end - start));
            sb.Append(@"\cf1\highlight0 ");
            index = end;
        }

        if (index < text.Length)
            AppendRtfEscaped(sb, text.AsSpan(index));

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendRtfEscaped(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                case '{':
                case '}':
                    sb.Append('\\').Append(ch);
                    break;
                case '\r':
                    break;
                case '\n':
                    sb.Append(@"\par ");
                    break;
                default:
                    if (ch <= 0x7f)
                    {
                        sb.Append(ch);
                    }
                    else
                    {
                        var code = ch > short.MaxValue ? ch - 65536 : ch;
                        sb.Append(@"\u").Append(code).Append('?');
                    }
                    break;
            }
        }
    }

    private void UpdateReadOnlyDocument(Action update)
    {
        ConversationDocument.IsReadOnly = false;
        try
        {
            update();
        }
        finally
        {
            ConversationDocument.IsReadOnly = true;
        }
    }

    private static ChatConversationDisplayText BuildDocumentText(IList? messages)
    {
        if (messages is null || messages.Count == 0)
            return new ChatConversationDisplayText();

        var parts = new List<ChatMessageDisplayPart>(messages.Count);
        foreach (ChatLineViewModel line in messages)
        {
            parts.Add(new ChatMessageDisplayPart
            {
                Header = line.Header ?? string.Empty,
                Text = line.Text ?? string.Empty,
                ReasoningText = line.ReasoningText ?? string.Empty,
                ApplySentenceBreaks = line.ApplySentenceBreaks,
            });
        }

        return ChatRichContentPlainText.BuildDisplayText(parts);
    }

    private void ApplyDocumentAppearance()
    {
        ConversationDocument.FontFamily = new FontFamily(_bodyFontFamily);
        ConversationDocument.FontSize = _bodyFontSize;
    }

    private void AttachDocumentScrollHost()
    {
        if (_documentScrollHost is not null)
            _documentScrollHost.ViewChanged -= OnDocumentScrollViewChanged;

        ConversationDocument.UpdateLayout();
        _documentScrollHost = FindDescendantScrollViewer(ConversationDocument);
        if (_documentScrollHost is not null)
            _documentScrollHost.ViewChanged += OnDocumentScrollViewChanged;
    }

    private void OnDocumentScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isProgrammaticScroll)
            return;

        _autoScrollToEnd = IsDocumentScrolledToBottom();
    }

    private bool IsDocumentScrolledToBottom()
    {
        if (_documentScrollHost is null)
            return true;

        return _documentScrollHost.VerticalOffset >= _documentScrollHost.ScrollableHeight - ScrollBottomTolerance;
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

    private void OnDocumentPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            _isPointerSelecting = true;
    }

    private void OnDocumentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerSelecting)
            return;

        _isPointerSelecting = false;
        RebuildNow();
    }

    private void ScheduleScrollToEnd()
    {
        if (_scrollScheduled || _isPointerSelecting || !_autoScrollToEnd)
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
