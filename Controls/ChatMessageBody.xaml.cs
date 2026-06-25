using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.Controls;

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

    private static readonly FontFamily ChatBodyFont = new("Segoe UI");
    private static readonly FontFamily CodeFontFamily = new("Cascadia Mono, Consolas, Courier New");
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
            _latestSourceText = SourceText;
            _latestHeaderText = HeaderText;
            _applySentenceBreaks = ApplySentenceBreaks;
            _useSecondaryForeground = UseSecondaryForeground;
            RebuildContentNow();
        };
        Unloaded += (_, _) => StopRebuildTimer();
    }

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
        ContentPanel.Children.Clear();

        try
        {
            if (!string.IsNullOrWhiteSpace(_latestHeaderText))
                ContentPanel.Children.Add(CreateTextBlock(_latestHeaderText));

            var blocks = ChatRichContentParser.ParseBlocks(_latestSourceText, _applySentenceBreaks);
            foreach (var block in blocks)
                ContentPanel.Children.Add(CreateBlockElement(block));
        }
        catch
        {
            ContentPanel.Children.Clear();
            if (!string.IsNullOrWhiteSpace(_latestSourceText) || !string.IsNullOrWhiteSpace(_latestHeaderText))
                ContentPanel.Children.Add(CreateTextBlock(BuildFallbackText()));
        }
    }

    private UIElement CreateBlockElement(ChatDisplayBlock block)
    {
        if (block.Kind == ChatDisplayBlockKind.Code)
        {
            return new Border
            {
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = CreateTextBlock(block.CodeText, monospace: true),
            };
        }

        var text = ChatRichContentPlainText.FormatBlock(block);
        return string.IsNullOrWhiteSpace(text)
            ? new Border { Height = 0 }
            : CreateTextBlock(text);
    }

    private string BuildFallbackText()
    {
        if (string.IsNullOrWhiteSpace(_latestHeaderText))
            return _latestSourceText;

        if (string.IsNullOrWhiteSpace(_latestSourceText))
            return _latestHeaderText;

        return $"{_latestHeaderText}\n\n{_latestSourceText}";
    }

    private TextBlock CreateTextBlock(string text, bool monospace = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = monospace ? CodeFontFamily : ChatBodyFont,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
        };
        ApplyVisualStyle(block);
        return block;
    }

    private void ApplyVisualStyle(TextBlock block)
    {
        if (!_useSecondaryForeground)
            return;

        block.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        block.FontSize = 12;
    }
}
