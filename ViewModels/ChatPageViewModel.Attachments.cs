using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
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

        var payload = bytes;
        if (ChatAttachmentFileRules.IsWebp(bytes))
            payload = await TranscodeToPngAsync(file);

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
            ImageBase64 = ChatAttachmentFileRules.ToDataUrl(payload),
            PreviewImage = bitmap,
            DisplayLine = file.Name,
        });

        NotifyAttachmentStateChanged();
    }

    public async Task AddClipboardBitmapAsync(IRandomAccessStream stream)
    {
        var loc = LocalizationService.Instance;
        if (!ImageAttachEnabled)
            throw new InvalidOperationException(ImageAttachHint ?? loc.Get("Chat.Attachment.VisionDisabled"));

        var png = await TranscodeStreamToPngAsync(stream);
        if (png.Length > 20 * 1024 * 1024)
            throw new InvalidOperationException(loc.Get("Chat.Attachment.TooLarge"));

        var name = loc.Get("Chat.Attachment.ClipboardImage");
        var existing = PendingAttachments.FirstOrDefault(a => a.Kind == ChatAttachmentKind.Image);
        if (existing is not null)
            PendingAttachments.Remove(existing);

        var bitmap = new BitmapImage();
        using (var preview = new InMemoryRandomAccessStream())
        {
            var writer = new DataWriter(preview);
            try
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            finally
            {
                writer.Dispose();
            }

            preview.Seek(0);
            await bitmap.SetSourceAsync(preview);
        }

        PendingAttachments.Add(new ChatAttachmentItemViewModel
        {
            Kind = ChatAttachmentKind.Image,
            FileName = name,
            ImageBase64 = ChatAttachmentFileRules.ToDataUrl(png),
            PreviewImage = bitmap,
            DisplayLine = name,
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

    public async Task RegisterUrlToRagAsync(string url, CancellationToken ct = default)
    {
        var result = await _rag.IngestUrlAsync(url.Trim(), ct);
        if (result.Chunks <= 0)
            throw new InvalidOperationException(
                LocalizationService.Instance.Format("Settings.Rag.Error.SkippedEmpty", url));

        var loc = LocalizationService.Instance;
        StatusText = loc.Format("Chat.Url.RegisterRagDone", result.Chunks);
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

    private static async Task<byte[]> TranscodeToPngAsync(StorageFile file)
    {
        using var input = await file.OpenReadAsync();
        return await TranscodeStreamToPngAsync(input);
    }

    private static async Task<byte[]> TranscodeStreamToPngAsync(IRandomAccessStream input)
    {
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(input);
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var bgra = pixels.DetachPixelData();

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX,
                decoder.DpiY,
                bgra);
            await encoder.FlushAsync();
            output.Seek(0);

            var size = (uint)output.Size;
            var png = new byte[size];
            using var reader = new DataReader(output);
            await reader.LoadAsync(size);
            reader.ReadBytes(png);
            return png;
        }
        catch (Exception ex) when (ex is not LocalizedServiceException)
        {
            throw new LocalizedServiceException("Chat.Attachment.ImageUnreadable");
        }
    }
}
