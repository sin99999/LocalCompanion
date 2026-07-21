using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace LocalCompanion.Controls;

/// <summary>単一メッセージ用 RichTextBlock（会話全体表示は ChatConversationView 側）。</summary>
public sealed partial class ChatMessageBody : UserControl
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText),
            typeof(string),
            typeof(ChatMessageBody),
            new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(ChatMessageBody),
            new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty ApplySentenceBreaksProperty =
        DependencyProperty.Register(
            nameof(ApplySentenceBreaks),
            typeof(bool),
            typeof(ChatMessageBody),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

    public static readonly DependencyProperty UseSecondaryForegroundProperty =
        DependencyProperty.Register(
            nameof(UseSecondaryForeground),
            typeof(bool),
            typeof(ChatMessageBody),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    private static readonly TimeSpan RebuildThrottle = TimeSpan.FromMilliseconds(80);

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private DispatcherQueueTimer? _rebuildTimer;
    private string _latestSourceText = string.Empty;
    private string _latestHeaderText = string.Empty;
    private bool _applySentenceBreaks = true;
    private bool _useSecondaryForeground;

    public ChatMessageBody()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ChatRichTextDocumentBuilder.AppearanceChanged += OnConversationAppearanceChanged;
            _latestSourceText = SourceText;
            _latestHeaderText = HeaderText;
            _applySentenceBreaks = ApplySentenceBreaks;
            _useSecondaryForeground = UseSecondaryForeground;
            RebuildContentNow();
        };
        Unloaded += (_, _) =>
        {
            ChatRichTextDocumentBuilder.AppearanceChanged -= OnConversationAppearanceChanged;
            StopRebuildTimer();
        };
    }

    public static void SetConversationAppearance(string fontFamily, double fontSize) =>
        ChatRichTextDocumentBuilder.SetAppearance(fontFamily, fontSize);

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public bool ApplySentenceBreaks
    {
        get => (bool)GetValue(ApplySentenceBreaksProperty);
        set => SetValue(ApplySentenceBreaksProperty, value);
    }

    public bool UseSecondaryForeground
    {
        get => (bool)GetValue(UseSecondaryForegroundProperty);
        set => SetValue(UseSecondaryForegroundProperty, value);
    }

    private void OnConversationAppearanceChanged() => ScheduleRebuildContent();

    private static void OnDisplayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatMessageBody body)
            body.ScheduleRebuildContent();
    }

    private void ScheduleRebuildContent()
    {
        _latestSourceText = SourceText;
        _latestHeaderText = HeaderText;
        _applySentenceBreaks = ApplySentenceBreaks;
        _useSecondaryForeground = UseSecondaryForeground;

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
        RebuildContentNow();
    }

    private void StopRebuildTimer()
    {
        if (_rebuildTimer is null)
            return;

        _rebuildTimer.Tick -= OnRebuildTimerTick;
        _rebuildTimer.Stop();
    }

    private void RebuildContentNow()
    {
        ContentHost.Blocks.Clear();
        ChatRichTextDocumentBuilder.ApplyHostStyle(ContentHost, _useSecondaryForeground);

        try
        {
            ChatRichTextDocumentBuilder.AppendMessage(
                ContentHost.Blocks,
                _latestHeaderText,
                reasoningText: null,
                _latestSourceText,
                _applySentenceBreaks,
                addLeadingSpacer: false);
        }
        catch
        {
            ContentHost.Blocks.Clear();
            if (!string.IsNullOrWhiteSpace(_latestSourceText) || !string.IsNullOrWhiteSpace(_latestHeaderText))
            {
                ContentHost.Blocks.Add(
                    ChatRichTextDocumentBuilder.CreateParagraph(
                        string.IsNullOrWhiteSpace(_latestHeaderText)
                            ? _latestSourceText
                            : $"{_latestHeaderText}\n\n{_latestSourceText}",
                        linkify: false,
                        secondary: _useSecondaryForeground,
                        bold: false));
            }
        }
    }
}
