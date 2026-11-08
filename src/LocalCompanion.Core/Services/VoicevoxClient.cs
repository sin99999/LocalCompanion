using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LocalCompanion.Localization;
using LocalCompanion.Models;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed class VoicevoxClient
{
    private readonly HttpClient _http;
    private readonly VoicevoxOptions _opt;
    private readonly ILogger<VoicevoxClient> _log;
    /// <summary>0 未確認、1 あり、2 なし。</summary>
    private int _streamingSupport;

    public VoicevoxClient(HttpClient http, IOptions<VoicevoxOptions> opt, ILogger<VoicevoxClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;
    }

    public string BaseUrl => _opt.BaseUrl.TrimEnd('/');

    public async Task<VoicevoxStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_opt.ProbeTimeoutSeconds));

            var versionResp = await _http.GetAsync($"{BaseUrl}/version", cts.Token);
            if (!versionResp.IsSuccessStatusCode)
            {
                var speakersOk = await TrySpeakersProbeAsync(cts.Token);
                if (!speakersOk)
                    return new VoicevoxStatusDto(false, true, false, BaseUrl, null, null);
            }

            string? version = null;
            if (versionResp.IsSuccessStatusCode)
            {
                var body = (await versionResp.Content.ReadAsStringAsync(cts.Token)).Trim();
                version = body.Trim('"');
            }

            return new VoicevoxStatusDto(
                true,
                true,
                false,
                BaseUrl,
                version,
                LocalizationService.Instance.Get("Voicevox.Status.Ready"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new VoicevoxStatusDto(false, true, false, BaseUrl, null, null);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "VOICEVOX probe failed");
            return new VoicevoxStatusDto(false, true, false, BaseUrl, null, null);
        }
    }

    private async Task<bool> TrySpeakersProbeAsync(CancellationToken ct)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/speakers", ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<VoicevoxSpeakerStyleDto>> ListSpeakersAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"{BaseUrl}/speakers", ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<VoicevoxSpeakerStyleDto>();
        foreach (var speaker in doc.RootElement.EnumerateArray())
        {
            var speakerName = speaker.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
            if (!speaker.TryGetProperty("styles", out var styles))
                continue;
            foreach (var style in styles.EnumerateArray())
            {
                if (!style.TryGetProperty("id", out var idEl))
                    continue;
                var styleName = style.TryGetProperty("name", out var sn) ? sn.GetString() ?? "?" : "?";
                list.Add(new VoicevoxSpeakerStyleDto(idEl.GetInt32(), speakerName, styleName));
            }
        }

        // VOICEVOX /speakers の配列順（公式のキャラ→スタイル順）をそのまま使う
        return list;
    }

    public async Task<byte[]?> SynthesizeAsync(
        string text,
        VoicevoxSettingsDto settings,
        bool autoSpeak = false,
        CancellationToken ct = default)
    {
        var maxChars = autoSpeak ? _opt.AutoSpeakMaxChars : _opt.MaxSpeakChars;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opt.SynthesisTimeoutSeconds));
        var synthBody = await BuildSynthesisBodyAsync(text, settings, maxChars, preferSentenceEnd: autoSpeak, cts.Token);
        if (synthBody is null)
            return null;

        var speaker = settings.SpeakerId;
        for (var attempt = 0; attempt < _opt.SynthesisMaxRetries; attempt++)
        {
            using var synthContent = new StringContent(synthBody, Encoding.UTF8, "application/json");
            using var synthResp = await _http.PostAsync($"{BaseUrl}/synthesis?speaker={speaker}", synthContent, cts.Token);
            if (synthResp.IsSuccessStatusCode)
                return await synthResp.Content.ReadAsByteArrayAsync(cts.Token);

            _log.LogDebug("VOICEVOX synthesis attempt {Attempt} failed: {Status}", attempt + 1, synthResp.StatusCode);
            if (attempt + 1 < _opt.SynthesisMaxRetries)
                await Task.Delay(800, cts.Token);
        }

        _log.LogWarning("VOICEVOX synthesis failed after retries");
        return null;
    }

    public void NoteEngineVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;
        Volatile.Write(ref _streamingSupport, SupportsStreamingVersion(version) ? 1 : 2);
    }

    internal static bool SupportsStreamingVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        version = version.Trim().Trim('"');
        var parts = version.Split('.');
        var major = parts.Length > 0 ? LeadingInt(parts[0]) : 0;
        var minor = parts.Length > 1 ? LeadingInt(parts[1]) : 0;
        var patch = parts.Length > 2 ? LeadingInt(parts[2]) : 0;
        if (major > 0)
            return true;
        if (minor > 26)
            return true;
        return minor == 26 && patch >= 0;
    }

    /// <summary>エンジン 0.26 以降の、作りながら返す合成。失敗したら null。</summary>
    public async Task<Stream?> OpenStreamingSynthesisAsync(
        string text,
        VoicevoxSettingsDto settings,
        CancellationToken ct = default)
    {
        if (Volatile.Read(ref _streamingSupport) == 2)
            return null;

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_opt.SynthesisTimeoutSeconds));
        HttpResponseMessage? resp = null;
        try
        {
            var body = await BuildSynthesisBodyAsync(
                text,
                settings,
                _opt.AutoSpeakMaxChars,
                preferSentenceEnd: true,
                timeout.Token);
            if (body is null)
            {
                timeout.Dispose();
                return null;
            }

            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BaseUrl}/streaming_synthesis?speaker={settings.SpeakerId}&segment_length=1")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode == 404)
                    Volatile.Write(ref _streamingSupport, 2);
                resp.Dispose();
                timeout.Dispose();
                return null;
            }

            Volatile.Write(ref _streamingSupport, 1);
            var inner = await resp.Content.ReadAsStreamAsync(timeout.Token);
            var stream = new ResponseBodyStream(resp, inner, timeout);
            resp = null;
            return stream;
        }
        catch
        {
            resp?.Dispose();
            timeout.Dispose();
            throw;
        }
    }

    private async Task<string?> BuildSynthesisBodyAsync(
        string text,
        VoicevoxSettingsDto settings,
        int maxChars,
        bool preferSentenceEnd,
        CancellationToken ct)
    {
        var prepared = PrepareSpeakText(text, maxChars, preferSentenceEnd);
        if (string.IsNullOrWhiteSpace(prepared))
            return null;

        var queryUrl = $"{BaseUrl}/audio_query?speaker={settings.SpeakerId}&text={Uri.EscapeDataString(prepared)}";
        string? queryJson = null;
        for (var attempt = 0; attempt < _opt.SynthesisMaxRetries; attempt++)
        {
            using var queryResp = await _http.PostAsync(queryUrl, null, ct);
            if (queryResp.IsSuccessStatusCode)
            {
                queryJson = await queryResp.Content.ReadAsStringAsync(ct);
                break;
            }

            _log.LogDebug("VOICEVOX audio_query attempt {Attempt} failed: {Status}", attempt + 1, queryResp.StatusCode);
            if (attempt + 1 < _opt.SynthesisMaxRetries)
                await Task.Delay(800, ct);
        }

        if (queryJson is null)
        {
            _log.LogWarning("VOICEVOX audio_query failed after retries");
            return null;
        }

        var queryNode = JsonNode.Parse(queryJson)?.AsObject();
        if (queryNode is null)
            return null;

        queryNode["speedScale"] = settings.SpeedScale;
        queryNode["pitchScale"] = settings.PitchScale;
        queryNode["intonationScale"] = settings.IntonationScale;
        queryNode["volumeScale"] = settings.VolumeScale;
        queryNode["prePhonemeLength"] = settings.PrePhonemeLength;
        queryNode["postPhonemeLength"] = settings.PostPhonemeLength;
        return queryNode.ToJsonString();
    }

    private static int LeadingInt(string part)
    {
        var n = 0;
        var any = false;
        foreach (var ch in part)
        {
            if (!char.IsDigit(ch))
                break;
            any = true;
            n = n * 10 + (ch - '0');
        }

        return any ? n : 0;
    }

    public IReadOnlyList<string> BuildSpeakChunks(string text)
    {
        var cleaned = CleanSpeakText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
            return Array.Empty<string>();

        if (cleaned.Length > _opt.MaxSpeakChars)
            cleaned = cleaned[.._opt.MaxSpeakChars];

        var chunkSize = Math.Max(40, _opt.AutoSpeakMaxChars);
        var chunks = new List<string>();
        var offset = 0;

        while (offset < cleaned.Length)
        {
            var remaining = cleaned.Length - offset;
            var take = Math.Min(chunkSize, remaining);
            var slice = cleaned.Substring(offset, take);

            if (take < remaining)
            {
                var cut = FindLastSentenceEnd(slice);
                if (cut >= 30)
                {
                    var piece = slice[..(cut + 1)].Trim();
                    if (piece.Length > 0)
                    {
                        chunks.Add(piece);
                        offset += cut + 1;
                        while (offset < cleaned.Length && char.IsWhiteSpace(cleaned[offset]))
                            offset++;
                        continue;
                    }
                }
            }

            var chunk = slice.Trim();
            if (chunk.Length > 0)
                chunks.Add(chunk);
            offset += take;
        }

        return chunks;
    }

    internal static string PrepareSpeakText(string text, int maxChars, bool preferSentenceEnd = false)
    {
        var t = CleanSpeakText(text);
        if (string.IsNullOrWhiteSpace(t))
            return "";

        if (t.Length <= maxChars)
            return t;

        var slice = t[..maxChars];
        if (preferSentenceEnd)
        {
            var cut = FindLastSentenceEnd(slice);
            if (cut >= 30)
                return slice[..(cut + 1)];
        }

        return slice + "…";
    }

    internal static string CleanSpeakText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var t = text.Replace("\r\n", "\n");
        t = Regex.Replace(t, "```[\\s\\S]*?```", " ", RegexOptions.Multiline);
        t = Regex.Replace(t, "`[^`]+`", " ");
        t = Regex.Replace(t, "\\*\\*|__|\\*|_|#+\\s*", "");
        t = Regex.Replace(t, "!\\[[^\\]]*\\]\\([^)]*\\)", " ");
        t = Regex.Replace(t, "\\[[^\\]]*\\]\\([^)]*\\)", " ");
        t = Regex.Replace(t, "<[^>]+>", " ");
        t = Regex.Replace(t, "[\\p{So}\\p{Sk}]", " ");
        return Regex.Replace(t, "\\s+", " ").Trim();
    }

    private static int FindLastSentenceEnd(string slice)
    {
        var cut = -1;
        foreach (var ch in new[] { '。', '！', '？', '.', '!', '?' })
        {
            var i = slice.LastIndexOf(ch);
            if (i > cut) cut = i;
        }
        return cut;
    }
}

internal sealed class ResponseBodyStream : Stream
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _inner;
    private readonly CancellationTokenSource _timeout;

    public ResponseBodyStream(HttpResponseMessage response, Stream inner, CancellationTokenSource timeout)
    {
        _response = response;
        _inner = inner;
        _timeout = timeout;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _response.Dispose();
            _timeout.Dispose();
        }

        base.Dispose(disposing);
    }
}
