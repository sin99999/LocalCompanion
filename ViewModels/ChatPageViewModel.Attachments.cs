using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LocalCompanion.ViewModels;

public partial class ChatPageViewModel
{
    public ObservableCollection<ChatAttachmentItemViewModel> PendingAttachments { get; } = new();

    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public async Task AddImageAttachmentAsync(StorageFile file)
    {
        var loc = LocalizationService.Instance;
        if (!ImageAttachEnabled)
            throw new InvalidOperationException(ImageAttachHint ?? loc.Get("Chat.Attachment.VisionDisabled"));
        var props = await file.GetBasicPropertiesAsync();
        if (props.Size > 20 * 1024 * 1024)
            throw new InvalidOperationException(loc.Get("Chat.Attachment.TooLarge"));

        var buffer = await FileIO.ReadBufferAsync(file);
        var bytes = new byte[buffer.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
            reader.ReadBytes(bytes);

        var existing = PendingAttachments.FirstOrDefault(a => a.Kind == ChatAttachmentKind.Image);
        if (existing is not null)
            PendingAttachments.Remove(existing);

        using var stream = await file.OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);

        PendingAttachments.Add(new ChatAttachmentItemViewModel
        {
            Kind = ChatAttachmentKind.Image,
            FileName = file.Name,
            ImageBase64 = Convert.ToBase64String(bytes),
            PreviewImage = bitmap,
            DisplayLine = file.Name,
        });

        NotifyAttachmentStateChanged();
    }

    public async Task AddTextAttachmentAsync(StorageFile file)
    {
        var loc = LocalizationService.Instance;
        var text = await FileIO.ReadTextAsync(file);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(loc.Get("Chat.Attachment.EmptyText"));

        AddTextAttachmentContent(file.Name, text);
    }

    public async Task AddUrlAttachmentAsync(string url, CancellationToken ct = default)
    {
        var (displayName, text) = await ChatUrlContentFetcher.FetchAsync(url, ct);
        AddTextAttachmentContent(displayName, text);
    }

    private void AddTextAttachmentContent(string fileName, string text)
    {
        var existing = PendingAttachments.FirstOrDefault(a => a.Kind == ChatAttachmentKind.TextFile);
        if (existing is not null)
            PendingAttachments.Remove(existing);

        PendingAttachments.Add(new ChatAttachmentItemViewModel
        {
            Kind = ChatAttachmentKind.TextFile,
            FileName = fileName,
            TextContent = text,
            DisplayLine = fileName,
        });

        NotifyAttachmentStateChanged();
    }

    [RelayCommand]
    private void RemoveAttachment(ChatAttachmentItemViewModel? item)
    {
        if (item is null)
            return;

        PendingAttachments.Remove(item);
        NotifyAttachmentStateChanged();
    }

    private void ClearPendingAttachments()
    {
        PendingAttachments.Clear();
        NotifyAttachmentStateChanged();
    }

    public void ClearPendingImageAttachments()
    {
        var removed = false;
        for (var i = PendingAttachments.Count - 1; i >= 0; i--)
        {
            if (PendingAttachments[i].Kind != ChatAttachmentKind.Image)
                continue;

            PendingAttachments.RemoveAt(i);
            removed = true;
        }

        if (removed)
            NotifyAttachmentStateChanged();
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingAttachments));
        SendCommand.NotifyCanExecuteChanged();
    }

    private string BuildUserDisplayMessage(string message)
    {
        if (PendingAttachments.Count == 0)
            return message;

        var loc = LocalizationService.Instance;
        var lines = PendingAttachments
            .Select(a => a.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (!string.IsNullOrWhiteSpace(message))
            lines.Add(message);

        return string.Join('\n', lines);
    }

    private (string[]? imagesBase64, string? attachedText, string? attachedFileName) TakePendingAttachmentsForRequest()
    {
        string[]? images = null;
        string? attachedText = null;
        string? attachedFileName = null;

        foreach (var item in PendingAttachments)
        {
            switch (item.Kind)
            {
                case ChatAttachmentKind.Image when !string.IsNullOrWhiteSpace(item.ImageBase64):
                    images = [item.ImageBase64];
                    break;
                case ChatAttachmentKind.TextFile when !string.IsNullOrWhiteSpace(item.TextContent):
                    attachedText = item.TextContent;
                    attachedFileName = item.FileName;
                    break;
            }
        }

        return (images, attachedText, attachedFileName);
    }
}
