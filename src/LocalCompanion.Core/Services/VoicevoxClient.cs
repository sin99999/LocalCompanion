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
        var prepared = PrepareSpeakText(text, maxChars, preferSentenceEnd: autoSpeak);
        if (string.IsNullOrWhiteSpace(prepared))
            return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opt.SynthesisTimeoutSeconds));

        var speaker = settings.SpeakerId;
        var queryUrl =
            $"{BaseUrl}/audio_query?speaker={speaker}&text={Uri.EscapeDataString(prepared)}";

        string? queryJson = null;
        for (var attempt = 0; attempt < _opt.SynthesisMaxRetries; attempt++)
        {
            using var queryResp = await _http.PostAsync(queryUrl, null, cts.Token);
            if (queryResp.IsSuccessStatusCode)
            {
                queryJson = await queryResp.Content.ReadAsStringAsync(cts.Token);
                break;
            }
            _log.LogDebug("VOICEVOX audio_query attempt {Attempt} failed: {Status}", attempt + 1, queryResp.StatusCode);
            if (attempt + 1 < _opt.SynthesisMaxRetries)
                await Task.Delay(800, cts.Token);
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

        var synthBody = queryNode.ToJsonString();
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
