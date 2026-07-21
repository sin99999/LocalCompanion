using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace LocalCompanion.Controls;

public sealed partial class ChatConversationView : UserControl
{
    public static readonly DependencyProperty MessagesProperty =
        DependencyProperty.Register(
            nameof(Messages),
            typeof(IList),
            typeof(ChatConversationView),
            new PropertyMetadata(null, OnMessagesPropertyChanged));

    private static readonly TimeSpan RebuildThrottle = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan StreamingRebuildThrottle = TimeSpan.FromMilliseconds(160);

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<ChatLineViewModel> _trackedLines = new();
    private IList? _messages;
    private bool _scrollScheduled;
    private bool _autoScrollToEnd = true;
    private DispatcherQueueTimer? _rebuildTimer;
    private bool _webReady;
    private bool _shellLoaded;
    private bool _runtimeUnavailable;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 14;
    private string? _webUserDataFolder;
    private WebView2? _conversationWeb;

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

    public void ApplyAppearance(string fontFamily, double fontSize)
    {
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        _fontSize = fontSize > 0 ? fontSize : 14;
        _ = ApplyAppearanceToWebAsync();
    }

    public void ScrollToEnd()
    {
        if (!_webReady || _conversationWeb is null)
            return;

        _ = _conversationWeb.ExecuteScriptAsync("lcScrollEnd();");
    }

    private static void OnMessagesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatConversationView view)
            view.AttachMessages(e.OldValue as IList, e.NewValue as IList);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!WebView2RuntimeAvailability.IsInstalled())
            {
                ShowRuntimeMissingPlaceholder();
                return;
            }

            await EnsureWebAsync();
            ScheduleRebuild(RebuildThrottle);
            ScheduleScrollToEnd();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"ChatConversationView WebView2 init failed: {ex.Message}");
            if (!WebView2RuntimeAvailability.IsInstalled())
                ShowRuntimeMissingPlaceholder();
        }
    }

    private void ShowRuntimeMissingPlaceholder()
    {
        _runtimeUnavailable = true;
        WebHost.Children.Clear();
        _conversationWeb = null;
        WebHost.Children.Add(new TextBlock
        {
            Text = LocalizationService.Instance.Get("WebView2.Missing.Placeholder"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(8, 12, 8, 8),
            Opacity = 0.85,
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachMessages(_messages, null);
        StopRebuildTimer();
        if (_conversationWeb?.CoreWebView2 is not null)
        {
            _conversationWeb.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            _conversationWeb.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            _conversationWeb.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        if (_conversationWeb is not null)
        {
            WebHost.Children.Remove(_conversationWeb);
            _conversationWeb.Close();
            _conversationWeb = null;
        }

        _webReady = false;
        _shellLoaded = false;
    }

    private async Task EnsureWebAsync()
    {
        if (_webReady || _runtimeUnavailable)
            return;

        if (!WebView2RuntimeAvailability.IsInstalled())
        {
            ShowRuntimeMissingPlaceholder();
            return;
        }

        _webUserDataFolder ??= Path.Combine(
            AppPaths.ResolveUserDataDirectory(null),
            "webview2-chat");
        Directory.CreateDirectory(_webUserDataFolder);

        if (_conversationWeb is null)
        {
            _conversationWeb = new WebView2
            {
                DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
            };
            WebHost.Children.Add(_conversationWeb);
        }

        // WinUI 投影の CreateAsync は WPF 版と引数が違うため、環境変数で UDF を指定する。
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", _webUserDataFolder);
        await _conversationWeb.EnsureCoreWebView2Async();
        _conversationWeb.CoreWebView2!.Settings.AreDefaultContextMenusEnabled = true;
        _conversationWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _conversationWeb.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _conversationWeb.CoreWebView2.Settings.IsZoomControlEnabled = false;
        _conversationWeb.CoreWebView2.NavigationStarting += OnNavigationStarting;
        _conversationWeb.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _conversationWeb.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var shell = ChatConversationHtmlBuilder.BuildShell(_fontFamily, _fontSize);
        _conversationWeb.NavigateToString(shell);
        _shellLoaded = true;
        _webReady = true;
        await Task.Delay(30);
        await PushLogAsync(scroll: true);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Uri)
            || args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        args.Cancel = true;
        _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() is not "scroll")
            {
                return;
            }

            if (doc.RootElement.TryGetProperty("atEnd", out var atEndEl)
                && atEndEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _autoScrollToEnd = atEndEl.GetBoolean();
            }
        }
        catch
        {
            /* ignore malformed host messages */
        }
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

        ScheduleRebuild(RebuildThrottle);
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

        var streaming = e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is { Count: > 0 }
            && _messages is not null
            && e.NewStartingIndex >= 0
            && e.NewStartingIndex + e.NewItems.Count == _messages.Count;

        ScheduleRebuild(streaming ? StreamingRebuildThrottle : RebuildThrottle);
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
        if (e.PropertyName is not (nameof(ChatLineViewModel.Text)
            or nameof(ChatLineViewModel.ReasoningText)
            or nameof(ChatLineViewModel.Header)))
        {
            return;
        }

        var streaming = sender is ChatLineViewModel line
            && _messages is not null
            && _messages.Count > 0
            && ReferenceEquals(_messages[_messages.Count - 1], line)
            && e.PropertyName is nameof(ChatLineViewModel.Text) or nameof(ChatLineViewModel.ReasoningText);

        ScheduleRebuild(streaming ? StreamingRebuildThrottle : RebuildThrottle);
        ScheduleScrollToEnd();
    }

    private void ScheduleRebuild(TimeSpan delay)
    {
        _rebuildTimer ??= _dispatcherQueue.CreateTimer();
        _rebuildTimer.Interval = delay;
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick -= OnRebuildTimerTick;
        _rebuildTimer.Tick += OnRebuildTimerTick;
        _rebuildTimer.Start();
    }

    private void OnRebuildTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= OnRebuildTimerTick;
        sender.Stop();
        _ = PushLogAsync(scroll: _autoScrollToEnd);
    }

    private void StopRebuildTimer()
    {
        if (_rebuildTimer is null)
            return;

        _rebuildTimer.Tick -= OnRebuildTimerTick;
        _rebuildTimer.Stop();
    }

    private async Task PushLogAsync(bool scroll)
    {
        if (_runtimeUnavailable || !_webReady || _conversationWeb?.CoreWebView2 is null)
            return;

        if (!_shellLoaded)
        {
            _conversationWeb.NavigateToString(ChatConversationHtmlBuilder.BuildShell(_fontFamily, _fontSize));
            _shellLoaded = true;
            await Task.Delay(30);
        }

        var lines = new List<ChatConversationHtmlBuilder.Line>();
        if (_messages is not null)
        {
            foreach (ChatLineViewModel line in _messages)
            {
                lines.Add(new ChatConversationHtmlBuilder.Line(
                    line.Header,
                    line.ReasoningText,
                    line.Text,
                    line.ApplySentenceBreaks));
            }
        }

        var html = ChatConversationHtmlBuilder.BuildLogHtml(lines);
        var payload = JsonSerializer.Serialize(html);
        var scrollJson = scroll ? "true" : "false";
        try
        {
            await _conversationWeb.ExecuteScriptAsync($"lcSetLog({payload}, {scrollJson});");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"ChatConversationView push failed: {ex.Message}");
        }
    }

    private async Task ApplyAppearanceToWebAsync()
    {
        if (!_webReady || _conversationWeb?.CoreWebView2 is null)
            return;

        var family = JsonSerializer.Serialize(_fontFamily);
        var size = _fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await _conversationWeb.ExecuteScriptAsync($"lcSetAppearance({family}, {size});");
        }
        catch
        {
            /* ignore */
        }
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
        });
    }
}
