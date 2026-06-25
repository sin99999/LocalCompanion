using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.ViewModels;

public enum ChatAttachmentKind
{
    Image,
    TextFile,
}

public partial class ChatAttachmentItemViewModel : ObservableObject
{
    public ChatAttachmentKind Kind { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string? TextContent { get; init; }

    public string? ImageBase64 { get; init; }

    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    public string DisplayLine { get; set; } = string.Empty;

    public Visibility ImageVisibility =>
        Kind == ChatAttachmentKind.Image ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TextVisibility =>
        Kind == ChatAttachmentKind.TextFile ? Visibility.Visible : Visibility.Collapsed;
}
