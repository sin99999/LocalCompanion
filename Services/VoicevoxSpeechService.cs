using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;
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
    private readonly VoicevoxOptions _opt;
    private readonly MediaPlayer _player = new();
    private readonly object _gate = new();
    private InMemoryRandomAccessStream? _playbackStream;
    private ReplySpeechCursor? _cursor;
    private SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _sessionCts;
    private string _latest = "";
    private bool _flush;
    private int _generation;
    private int _startedGen = -1;
    private int _settingsGen = -1;
    private VoicevoxSettingsDto? _settingsCache;

    public VoicevoxSpeechService(
        VoicevoxLifecycleService lifecycle,
        VoicevoxClient client,
        VoicevoxSettingsStore settings,
        VoicevoxInstallLocator locator,
        ChatService chat,
        IOptions<VoicevoxOptions> options)
    {
        _lifecycle = lifecycle;
        _client = client;
        _settings = settings;
        _locator = locator;
        _chat = chat;
        _opt = options.Value;
    }

    /// <summary>本文が外国語のままなので、訳してからまとめて読む。</summary>
    public bool ShouldSpeakTranslated { get; private set; }

    public void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            _latest = "";
            _flush = false;
            _cursor = null;
            ShouldSpeakTranslated = false;
            cts = _sessionCts;
            _sessionCts = null;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }
        try { _player.Pause(); } catch { /* ignore */ }
    }

    /// <summary>返事の本文が伸びたとき。推論は渡さない。</summary>
    public void NoteReplyText(string replySoFar)
    {
        if (string.IsNullOrWhiteSpace(replySoFar))
            return;

        var gen = Volatile.Read(ref _generation);
        var settings = SettingsFor(gen);
        if (!CanSpeak(settings))
        {
            ShouldSpeakTranslated = false;
            return;
        }

        if (settings.SpeakInJapanesePronunciation && !TextScriptHelper.LooksJapanese(replySoFar))
        {
            ShouldSpeakTranslated = true;
            return;
        }

        ShouldSpeakTranslated = false;
        Remember(gen, replySoFar, flush: false);
        EnsureSession(gen, settings);
        Wake();
    }

    /// <summary>返事が揃ったとき。残りの文を読み、ループを終える。</summary>
    public void FinishReply(string? finalReply)
    {
        var gen = Volatile.Read(ref _generation);
        var settings = SettingsFor(gen);
        if (!CanSpeak(settings))
        {
            ShouldSpeakTranslated = false;
            return;
        }

        var text = finalReply ?? "";
        var defer = settings.SpeakInJapanesePronunciation && !TextScriptHelper.LooksJapanese(text);
        if (defer && _startedGen != gen)
        {
            ShouldSpeakTranslated = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && _startedGen != gen)
            return;

        ShouldSpeakTranslated = false;
        Remember(gen, text, flush: true);
        EnsureSession(gen, settings);
        Wake();
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

            await SpeakSlicesAsync(speakText, settings, gen, ct);
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

    private void Remember(int gen, string text, bool flush)
    {
        lock (_gate)
        {
            if (gen != _generation)
                return;
            _latest = text;
            if (flush)
                _flush = true;
        }
    }

    private void EnsureSession(int gen, VoicevoxSettingsDto settings)
    {
        lock (_gate)
        {
            if (gen != _generation || _startedGen == gen)
                return;

            _startedGen = gen;
            _cursor = new ReplySpeechCursor(
                Math.Max(40, _opt.AutoSpeakMaxChars),
                Math.Max(40, _opt.MaxSpeakChars));
            var cts = new CancellationTokenSource();
            _sessionCts = cts;
            var token = cts.Token;
            var wake = new SemaphoreSlim(0, 1);
            _wake = wake;
            var snapshot = settings;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunSessionAsync(snapshot, gen, wake, token);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_sessionCts, cts))
                            _sessionCts = null;
                    }

                    cts.Dispose();
                }
            });
        }
    }

    private void Wake()
    {
        SemaphoreSlim wake;
        lock (_gate)
            wake = _wake;
        try { wake.Release(); } catch (SemaphoreFullException) { /* すでに起きている */ }
    }

    private VoicevoxSettingsDto SettingsFor(int gen)
    {
        lock (_gate)
        {
            if (_settingsGen == gen && _settingsCache is not null)
                return _settingsCache;
            _settingsCache = _settings.Load();
            _settingsGen = gen;
            return _settingsCache;
        }
    }

    private bool CanSpeak(VoicevoxSettingsDto settings) =>
        settings.Enabled && settings.AutoSpeak && _locator.IsInstalled;

    private async Task RunSessionAsync(
        VoicevoxSettingsDto settings,
        int gen,
        SemaphoreSlim wake,
        CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var playTask = PlayChannelAsync(channel.Reader, gen, ct);
        try
        {
            var status = await _lifecycle.EnsureRunningAsync(ct);
            if (!status.Available || gen != Volatile.Read(ref _generation))
                return;

            _client.NoteEngineVersion(status.Version);
            while (gen == Volatile.Read(ref _generation) && !ct.IsCancellationRequested)
            {
                List<string> slices;
                bool flush;
                lock (_gate)
                {
                    slices = _cursor?.Push(_latest, _flush).ToList() ?? new List<string>();
                    flush = _flush;
                }

                if (slices.Count == 0)
                {
                    if (flush)
                        break;
                    await wake.WaitAsync(ct);
                    continue;
                }

                foreach (var slice in slices)
                {
                    if (gen != Volatile.Read(ref _generation))
                        return;
                    try
                    {
                        await ProduceSliceAsync(slice, settings, channel.Writer, gen, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        /* この文は飛ばして、次の文へ */
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* 停止 */
        }
        catch
        {
            /* 読み上げ失敗はチャットを止めない */
        }
        finally
        {
            channel.Writer.TryComplete();
            try { await playTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* 停止 */ }
        }
    }

    private async Task ProduceSliceAsync(
        string slice,
        VoicevoxSettingsDto settings,
        ChannelWriter<byte[]> writer,
        int gen,
        CancellationToken ct)
    {
        var wave = await _client.OpenStreamingSynthesisAsync(slice, settings, ct);
        if (wave is not null)
        {
            await using (wave)
            {
                if (await PumpStreamAsync(wave, writer, gen, ct))
                    return;
            }
        }

        if (gen != Volatile.Read(ref _generation))
            return;

        var wav = await _client.SynthesizeAsync(slice, settings, autoSpeak: false, ct);
        if (wav is null || wav.Length == 0 || gen != Volatile.Read(ref _generation))
            return;
        await writer.WriteAsync(wav, ct);
    }

    private async Task<bool> PumpStreamAsync(
        Stream wave,
        ChannelWriter<byte[]> writer,
        int gen,
        CancellationToken ct)
    {
        var reader = new WavPcmReader(wave);
        var bucket = new PcmBucket();
        var buf = new byte[8192];
        var queued = false;
        while (true)
        {
            var n = await reader.ReadPcmAsync(buf, ct);
            if (!reader.IsPcm16)
                return false;
            if (n <= 0)
                break;
            if (gen != Volatile.Read(ref _generation))
                return true;

            bucket.Append(buf.AsSpan(0, n));
        }

        while (SpeechWavPieces.Pull(bucket, reader.SampleRate, reader.Channels, flush: true) is byte[] tail)
        {
            await writer.WriteAsync(tail, ct);
            queued = true;
        }

        return queued;
    }

    private async Task PlayChannelAsync(ChannelReader<byte[]> reader, int gen, CancellationToken ct)
    {
        try
        {
            await foreach (var wav in reader.ReadAllAsync(ct))
            {
                if (gen != Volatile.Read(ref _generation))
                    return;
                if (wav is null || wav.Length == 0)
                    continue;
                await PlayWavAsync(wav, gen, ct);
            }
        }
        catch (OperationCanceledException)
        {
            /* 停止 */
        }
    }

    private async Task SpeakSlicesAsync(string fullText, VoicevoxSettingsDto settings, int gen, CancellationToken ct)
    {
        var cursor = new ReplySpeechCursor(
            Math.Max(40, _opt.AutoSpeakMaxChars),
            Math.Max(40, _opt.MaxSpeakChars));
        var slices = cursor.Push(fullText, flush: true);
        if (slices.Count == 0)
            return;

        _client.NoteEngineVersion((await _lifecycle.GetStatusAsync(ct)).Version);
        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var playTask = PlayChannelAsync(channel.Reader, gen, ct);
        try
        {
            foreach (var slice in slices)
            {
                if (gen != Volatile.Read(ref _generation))
                    return;
                await ProduceSliceAsync(slice, settings, channel.Writer, gen, ct);
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            try { await playTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* 停止 */ }
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

        _ = PlayWavCoreAsync(wav, gen, tcs, Cleanup);
        return tcs.Task;
    }

    private async Task PlayWavCoreAsync(byte[] wav, int gen, TaskCompletionSource tcs, Action cleanup)
    {
        try
        {
            if (gen != _generation)
            {
                cleanup();
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
                cleanup();
                tcs.TrySetResult();
                return;
            }

            _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            _player.Play();
        }
        catch
        {
            cleanup();
            tcs.TrySetResult();
        }
    }
}
