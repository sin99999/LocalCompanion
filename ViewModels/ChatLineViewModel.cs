using CommunityToolkit.Mvvm.ComponentModel;
using LocalCompanion.Localization;
using Microsoft.UI.Xaml;

namespace LocalCompanion.ViewModels;

public partial class ChatLineViewModel : ObservableObject
{
    public ChatLineViewModel(string role, string text, string? assistantLabel = null, bool isWelcomePlaceholder = false)
    {
        Role = role;
        Text = text;
        AssistantLabel = assistantLabel;
        IsWelcomePlaceholder = isWelcomePlaceholder;
    }

    public string Role { get; }

    public string? AssistantLabel { get; }

    public bool IsWelcomePlaceholder { get; }

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>assistant のみ句点改行・リスト・表のリッチ表示。</summary>
    public bool ApplySentenceBreaks => Role == "assistant";

    public bool UseRichDisplay => Role == "assistant";

    public Visibility PlainTextVisibility =>
        UseRichDisplay ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RichDisplayVisibility =>
        UseRichDisplay ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReasoningVisibility))]
    public partial string ReasoningText { get; set; } = string.Empty;

    public Visibility ReasoningVisibility =>
        string.IsNullOrWhiteSpace(ReasoningText) ? Visibility.Collapsed : Visibility.Visible;

    public void SetText(string value) => Text = value;

    public void SetReasoning(string value) => ReasoningText = value;

    public void ClearReasoning() => ReasoningText = string.Empty;

    public void AppendText(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return;
        Text = string.Concat(Text, chunk);
    }

    public string? UserLabel { get; private set; }

    public void SetUserLabel(string? label)
    {
        UserLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        OnPropertyChanged(nameof(Header));
    }

    public string Header => Role switch
    {
        "user" => string.IsNullOrWhiteSpace(UserLabel)
            ? LocalizationService.Instance.Get("Chat.UserLabel")
            : UserLabel,
        "assistant" => string.IsNullOrWhiteSpace(AssistantLabel)
            ? LocalizationService.Instance.Get("Chat.Assistant.DefaultName")
            : AssistantLabel,
        _ => Role,
    };

    public void RefreshLocalization() => OnPropertyChanged(nameof(Header));
}
