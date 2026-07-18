using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

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

    private static readonly FontFamily CodeFontFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly TimeSpan RebuildThrottle = TimeSpan.FromMilliseconds(80);

    private static string ConversationFontFamily = "Segoe UI";
    private static double ConversationFontSize = 14;
    private static event Action? ConversationAppearanceChanged;

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
            ConversationAppearanceChanged += OnConversationAppearanceChanged;
            _latestSourceText = SourceText;
            _latestHeaderText = HeaderText;
            _applySentenceBreaks = ApplySentenceBreaks;
            _useSecondaryForeground = UseSecondaryForeground;
            RebuildContentNow();
        };
        Unloaded += (_, _) =>
        {
            ConversationAppearanceChanged -= OnConversationAppearanceChanged;
            StopRebuildTimer();
        };
    }

    public static void SetConversationAppearance(string fontFamily, double fontSize)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        var size = fontSize > 0 ? fontSize : 14;
        if (string.Equals(ConversationFontFamily, family, StringComparison.Ordinal)
            && Math.Abs(ConversationFontSize - size) < 0.01)
        {
            return;
        }

        ConversationFontFamily = family;
        ConversationFontSize = size;
        ConversationAppearanceChanged?.Invoke();
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
        ApplyHostStyle();

        try
        {
            if (!string.IsNullOrWhiteSpace(_latestHeaderText))
                ContentHost.Blocks.Add(CreateParagraph(_latestHeaderText, linkify: false));

            var blocks = ChatRichContentParser.ParseBlocks(_latestSourceText, _applySentenceBreaks);
            foreach (var block in blocks)
                AddDisplayBlock(block);

            if (ContentHost.Blocks.Count == 0
                && (!string.IsNullOrWhiteSpace(_latestSourceText) || !string.IsNullOrWhiteSpace(_latestHeaderText)))
            {
                ContentHost.Blocks.Add(CreateParagraph(BuildFallbackText(), linkify: true));
            }
        }
        catch
        {
            ContentHost.Blocks.Clear();
            if (!string.IsNullOrWhiteSpace(_latestSourceText) || !string.IsNullOrWhiteSpace(_latestHeaderText))
                ContentHost.Blocks.Add(CreateParagraph(BuildFallbackText(), linkify: false));
        }
    }

    private void AddDisplayBlock(ChatDisplayBlock block)
    {
        if (block.Kind == ChatDisplayBlockKind.Code)
        {
            ContentHost.Blocks.Add(CreateCodeParagraph(block.CodeText));
            return;
        }

        var text = ChatRichContentPlainText.FormatBlock(block);
        if (string.IsNullOrWhiteSpace(text))
            return;

        ContentHost.Blocks.Add(CreateParagraph(text, linkify: true));
    }

    private Paragraph CreateCodeParagraph(string codeText)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
        };

        var border = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = codeText,
                FontFamily = CodeFontFamily,
                FontSize = Math.Max(12, ConversationFontSize - 1),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
            },
        };

        // コード枠は InlineUIContainer（本文 RichTextBlock 内に置き、上下の選択は本文側で連続）
        paragraph.Inlines.Add(new InlineUIContainer { Child = border });
        return paragraph;
    }

    private Paragraph CreateParagraph(string text, bool linkify)
    {
        var paragraph = new Paragraph();
        AppendTextInlines(paragraph.Inlines, text, linkify, monospace: false);
        return paragraph;
    }

    private void AppendTextInlines(InlineCollection inlines, string text, bool linkify, bool monospace)
    {
        var fontFamily = monospace ? CodeFontFamily : new FontFamily(ConversationFontFamily);
        var fontSize = monospace ? Math.Max(12, ConversationFontSize - 1) : ConversationFontSize;

        if (!linkify || text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
        {
            inlines.Add(new Run
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
            });
            return;
        }

        foreach (var segment in ChatMessageUrlExtractor.SplitByUrls(text))
        {
            if (segment.IsUrl
                && Uri.TryCreate(segment.Text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run
                {
                    Text = segment.Text,
                    FontFamily = fontFamily,
                    FontSize = fontSize,
                });
                var target = uri;
                link.Click += async (_, _) =>
                {
                    try
                    {
                        await Windows.System.Launcher.LaunchUriAsync(target);
                    }
                    catch
                    {
                        /* 起動失敗は無視 */
                    }
                };
                inlines.Add(link);
            }
            else
            {
                inlines.Add(new Run
                {
                    Text = segment.Text,
                    FontFamily = fontFamily,
                    FontSize = fontSize,
                });
            }
        }
    }

    private string BuildFallbackText()
    {
        if (string.IsNullOrWhiteSpace(_latestHeaderText))
            return _latestSourceText;

        if (string.IsNullOrWhiteSpace(_latestSourceText))
            return _latestHeaderText;

        return $"{_latestHeaderText}\n\n{_latestSourceText}";
    }

    private void ApplyHostStyle()
    {
        ContentHost.FontFamily = new FontFamily(ConversationFontFamily);
        ContentHost.FontSize = ConversationFontSize;
        if (_useSecondaryForeground)
        {
            ContentHost.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            ContentHost.FontSize = Math.Max(12, ConversationFontSize - 2);
        }
        else
        {
            ContentHost.ClearValue(RichTextBlock.ForegroundProperty);
        }
    }
}
