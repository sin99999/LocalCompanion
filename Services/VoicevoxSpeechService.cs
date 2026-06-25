using System.Runtime.InteropServices.WindowsRuntime;
using LocalCompanion.Models;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace LocalCompanion.Services;

/// <summary>VOICEVOX 合成とチャット読み上げ再生。</summary>
public sealed class VoicevoxSpeechService
{
    private readonly VoicevoxLifecycleService _lifecycle;
    private readonly VoicevoxClient _client;
    private readonly VoicevoxSettingsStore _settings;
    private readonly VoicevoxInstallLocator _locator;
    private readonly ChatService _chat;
    private readonly MediaPlayer _player = new();
    private InMemoryRandomAccessStream? _playbackStream;
    private int _generation;

    public VoicevoxSpeechService(
        VoicevoxLifecycleService lifecycle,
        VoicevoxClient client,
        VoicevoxSettingsStore settings,
        VoicevoxInstallLocator locator,
        ChatService chat)
    {
        _lifecycle = lifecycle;
        _client = client;
        _settings = settings;
        _locator = locator;
        _chat = chat;
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _generation);
        try
        {
            _player.Pause();
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task MaybeSpeakAssistantAsync(string text, CancellationToken ct = default)
    {
        var settings = _settings.Load();
        if (!settings.Enabled || !settings.AutoSpeak)
            return;
        if (!_locator.IsInstalled)
            return;

        var body = text.Trim();
        if (body.Length == 0)
            return;

        var status = await _lifecycle.EnsureRunningAsync(ct);
        if (!status.Available)
            return;

        Cancel();
        var gen = _generation;

        try
        {
            var speakText = body;
            if (settings.SpeakInJapanesePronunciation && !TextScriptHelper.LooksJapanese(body))
            {
                var japanese = await _chat.TranslateForJapaneseSpeechAsync(body, ct);
                if (gen != _generation)
                    return;
                if (string.IsNullOrWhiteSpace(japanese))
                    return;
                speakText = japanese.Trim();
            }

            await SpeakQueuedAsync(speakText, settings, gen, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            /* ignore */
        }
        catch
        {
            /* 読み上げ失敗はチャットを止めない */
        }
    }

    private async Task SpeakQueuedAsync(string fullText, VoicevoxSettingsDto settings, int gen, CancellationToken ct)
    {
        var chunks = _client.BuildSpeakChunks(fullText);
        if (chunks.Count == 0)
            return;

        Task<byte[]?>? nextTask = _client.SynthesizeAsync(chunks[0], settings, autoSpeak: false, ct);
        for (var i = 0; i < chunks.Count; i++)
        {
            if (gen != _generation)
                return;

            var wav = await nextTask!;
            if (i + 1 < chunks.Count)
                nextTask = _client.SynthesizeAsync(chunks[i + 1], settings, autoSpeak: false, ct);

            if (wav is null || wav.Length == 0)
                continue;
            if (gen != _generation)
                return;

            await PlayWavAsync(wav, gen, ct);
        }
    }

    private Task PlayWavAsync(byte[] wav, int gen, CancellationToken ct)
    {
        if (gen != _generation)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Cleanup()
        {
            _player.MediaEnded -= OnEnded;
            _player.MediaFailed -= OnFailed;
        }

        void OnEnded(MediaPlayer sender, object args)
        {
            Cleanup();
            _playbackStream?.Dispose();
            _playbackStream = null;
            tcs.TrySetResult();
        }

        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            Cleanup();
            tcs.TrySetResult();
        }

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                Cleanup();
                tcs.TrySetResult();
            });
        }

        _player.MediaEnded += OnEnded;
        _player.MediaFailed += OnFailed;

        _ = PlayWavCoreAsync(wav, gen, tcs);
        return tcs.Task;
    }

    private async Task PlayWavCoreAsync(byte[] wav, int gen, TaskCompletionSource tcs)
    {
        try
        {
            if (gen != _generation)
            {
                tcs.TrySetResult();
                return;
            }

            _playbackStream?.Dispose();
            var stream = new InMemoryRandomAccessStream();
            _playbackStream = stream;
            await stream.WriteAsync(wav.AsBuffer());
            stream.Seek(0);

            if (gen != _generation)
            {
                tcs.TrySetResult();
                return;
            }

            _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            _player.Play();
        }
        catch
        {
            tcs.TrySetResult();
        }
    }
}
