using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalCompanion.Localization;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

public sealed record ChatTurn(string Role, string Content, string[]? ImagesBase64 = null);
public sealed record LlamaStreamPiece(string Kind, string Text);

public sealed class LlamaServerClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly LlamaOptions _opt;
    private readonly ModelCatalogService _models;
    private readonly ILogger<LlamaServerClient> _log;

    public LlamaServerClient(
        HttpClient http,
        IOptions<LlamaOptions> opt,
        ModelCatalogService models,
        ILogger<LlamaServerClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _models = models;
        _log = log;
        _http.BaseAddress = new Uri(_opt.LlamaServerBaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(15);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var res = await _http.GetAsync("health", timeout.Token);
            if (res.IsSuccessStatusCode) return true;
            res = await _http.GetAsync("v1/models", timeout.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var fromServer = await FetchModelIdsFromServerAsync(ct);
        if (fromServer.Count > 0)
            return fromServer;

        if (!string.IsNullOrWhiteSpace(_opt.LlamaModel))
            return new[] { _opt.LlamaModel.Trim() };

        var path = _models.ResolveModelPath();
        if (!string.IsNullOrWhiteSpace(path))
            return new[] { path };

        var selected = _models.GetSelection().ModelFileName;
        if (!string.IsNullOrWhiteSpace(selected))
            return new[] { selected };

        return Array.Empty<string>();
    }

    /// <summary>chat/completions・embeddings に渡す model ID（"local" は使わない）。</summary>
    public async Task<string> ResolveModelIdAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_opt.LlamaModel))
            return _opt.LlamaModel.Trim();

        var ids = (await ListModelsAsync(ct))
            .Where(id => !string.Equals(id, "local", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ids.Count == 1)
            return ids[0];

        var selected = ModelCatalogService.NormalizeModelFileName(_models.GetSelection().ModelFileName);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var match = ids.FirstOrDefault(id =>
                string.Equals(ModelCatalogService.NormalizeModelFileName(id), selected, StringComparison.OrdinalIgnoreCase)
                || id.EndsWith(selected, StringComparison.OrdinalIgnoreCase)
                || id.Contains(selected, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        var modelPath = _models.ResolveModelPath();
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var pathMatch = ids.FirstOrDefault(id =>
                string.Equals(id, modelPath, StringComparison.OrdinalIgnoreCase)
                || id.EndsWith(Path.GetFileName(modelPath), StringComparison.OrdinalIgnoreCase));
            if (pathMatch is not null)
                return pathMatch;
        }

        if (!string.IsNullOrWhiteSpace(selected) && ids.Count == 0)
        {
            _log.LogWarning(
                "v1/models が空のためファイル名で model を指定します。チャット失敗時は llama-server を再起動してください: {Model}",
                selected);
            return selected;
        }

        if (ids.Count > 0)
            return ids[0];

        throw new LocalizedServiceException("Error.LlamaModelIdUnavailable");
    }

    private async Task<IReadOnlyList<string>> FetchModelIdsFromServerAsync(CancellationToken ct)
    {
        try
        {
            var doc = await _http.GetFromJsonAsync<JsonElement>("v1/models", ct);
            var list = new List<string>();

            if (doc.TryGetProperty("data", out var data))
            {
                foreach (var m in data.EnumerateArray())
                {
                    if (!m.TryGetProperty("id", out var id))
                        continue;
                    var name = id.GetString();
                    if (!string.IsNullOrWhiteSpace(name)
                        && !string.Equals(name, "local", StringComparison.OrdinalIgnoreCase))
                        list.Add(name);
                }
            }

            if (list.Count == 0 && doc.TryGetProperty("models", out var legacy))
            {
                foreach (var m in legacy.EnumerateArray())
                {
                    string? name = null;
                    if (m.TryGetProperty("name", out var n))
                        name = n.GetString();
                    else if (m.TryGetProperty("model", out var mo))
                        name = mo.GetString();
                    if (!string.IsNullOrWhiteSpace(name)
                        && !string.Equals(name, "local", StringComparison.OrdinalIgnoreCase))
                        list.Add(name);
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "v1/models failed");
            return Array.Empty<string>();
        }
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatTurn> messages,
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        bool useReasoning = false,
        CancellationToken ct = default)
    {
        var model = await ResolveModelIdAsync(ct);

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(BuildMessage).ToArray(),
            ["temperature"] = temperature ?? _opt.Temperature,
            ["top_p"] = topP ?? _opt.TopP,
            ["max_tokens"] = maxTokens ?? _opt.MaxOutputTokens,
            ["stream"] = false
        };
        ApplyTopK(body, topK);
        ApplyRepetitionGuard(body);
        if (useReasoning)
        {
            body["reasoning_effort"] = "medium";
            body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = true };
        }
        else
        {
            // Gemma 4: 思考トークンだけ消費して content が空になるのを防ぐ
            body["reasoning_effort"] = "none";
            body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json")
        };
        HttpResponseMessage res;
        string raw;
        try
        {
            res = await _http.SendAsync(req, ct);
            raw = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            throw ConnectionFailed(ex);
        }
        if (!res.IsSuccessStatusCode)
        {
            if (IsContextOverflowError(raw))
            {
                throw new InvalidOperationException(ContextOverflowMessage);
            }
            if (messages.Any(m => m.ImagesBase64 is { Length: > 0 }) && raw.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new LocalizedServiceException("Chat.Error.VisionFailed");
            }
            throw new InvalidOperationException(
                Localization.LocalizationService.Instance.Format("Chat.Error.LlamaServer", (int)res.StatusCode, raw));
        }

        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            var msg = choice.GetProperty("message");
            var text = ExtractMessageText(msg);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
            _log.LogWarning(
                "llama-server: 空の応答 (finish_reason={Finish}). raw={Raw}",
                finish ?? "?",
                raw.Length > 500 ? raw[..500] + "…" : raw);
        }

        return "";
    }

    public async IAsyncEnumerable<LlamaStreamPiece> StreamChatAsync(
        IReadOnlyList<ChatTurn> messages,
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        bool useReasoning = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = await ResolveModelIdAsync(ct);

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(BuildMessage).ToArray(),
            ["temperature"] = temperature ?? _opt.Temperature,
            ["top_p"] = topP ?? _opt.TopP,
            ["max_tokens"] = maxTokens ?? _opt.MaxOutputTokens,
            ["stream"] = true
        };
        ApplyTopK(body, topK);
        ApplyRepetitionGuard(body);
        if (useReasoning)
        {
            body["reasoning_effort"] = "medium";
            body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = true };
        }
        else
        {
            body["reasoning_effort"] = "none";
            body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json")
        };

        using var res = await SendChatStreamRequestAsync(req, ct);

        if (!res.IsSuccessStatusCode)
        {
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (IsContextOverflowError(raw))
                throw new InvalidOperationException(ContextOverflowMessage);
            throw new InvalidOperationException(
                Localization.LocalizationService.Instance.Format("Chat.Error.LlamaServer", (int)res.StatusCode, raw));
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line[6..].Trim();
            if (payload == "[DONE]") break;

            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(payload);
                var root = json.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;
                if (delta.ValueKind == JsonValueKind.Undefined) continue;

                if (delta.TryGetProperty("reasoning_content", out var r))
                {
                    var rt = ReadContentElement(r);
                    if (!string.IsNullOrWhiteSpace(rt))
                        yield return new LlamaStreamPiece("reasoning", rt);
                }
                if (delta.TryGetProperty("reasoning", out var rr))
                {
                    var rt = ReadContentElement(rr);
                    if (!string.IsNullOrWhiteSpace(rt))
                        yield return new LlamaStreamPiece("reasoning", rt);
                }
                if (delta.TryGetProperty("content", out var c))
                {
                    var ctText = ReadContentElement(c);
                    if (!string.IsNullOrWhiteSpace(ctText))
                        yield return new LlamaStreamPiece("content", ctText);
                }
            }
            finally
            {
                json?.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> SendChatStreamRequestAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            throw ConnectionFailed(ex);
        }
    }

    public async Task<bool> EmbeddingsSupportedAsync(CancellationToken ct = default)
    {
        var model = _opt.EmbedModel;
        if (string.IsNullOrWhiteSpace(model))
            model = await ResolveModelIdAsync(ct);

        var body = new { model, input = "ping" };
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json")
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var res = await _http.SendAsync(req, timeout.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var model = _opt.EmbedModel;
        if (string.IsNullOrWhiteSpace(model))
            model = await ResolveModelIdAsync(ct);

        var body = new { model, input = text };
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json")
        };
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            _log.LogDebug(ex, "embeddings: llama-server unreachable");
            return null;
        }
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            return null;
        var emb = data[0].GetProperty("embedding");
        var arr = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var v in emb.EnumerateArray())
            arr[i++] = (float)v.GetDouble();
        return arr;
    }

    public Task WarmAsync(CancellationToken ct = default)
        => ChatAsync(new[] { new ChatTurn("user", " ") }, temperature: 0.1, topP: 0.9, maxTokens: 1, useReasoning: false, ct: ct);

    private static string ExtractMessageText(JsonElement msg)
    {
        if (msg.TryGetProperty("content", out var content))
        {
            var fromContent = ReadContentElement(content);
            if (!string.IsNullOrWhiteSpace(fromContent))
                return fromContent;
        }

        foreach (var key in new[] { "reasoning_content", "reasoning" })
        {
            if (!msg.TryGetProperty(key, out var r)) continue;
            var fromReasoning = ReadContentElement(r);
            if (!string.IsNullOrWhiteSpace(fromReasoning))
                return fromReasoning;
        }

        return "";
    }

    private static string ReadContentElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        if (el.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        foreach (var part in el.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
            else if (part.ValueKind == JsonValueKind.String)
                sb.Append(part.GetString());
        }
        return sb.ToString();
    }

    internal static string ContextOverflowMessage =>
        Localization.LocalizationService.Instance.Get("Error.ContextOverflow");

    internal static bool IsContextOverflowError(string raw) =>
        raw.Contains("exceed_context_size", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("exceeds the available context", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("Context size has been exceeded", StringComparison.OrdinalIgnoreCase);

    internal static bool IsContextOverflowException(InvalidOperationException ex) =>
        IsContextOverflowError(ex.Message) || ex.Message == ContextOverflowMessage;

    internal static string ConnectionFailedMessage =>
        Localization.LocalizationService.Instance.Get("Error.LlamaConnectionFailed");

    private static bool IsConnectionError(Exception ex) =>
        ex is HttpRequestException or System.Net.Sockets.SocketException;

    private static InvalidOperationException ConnectionFailed(Exception ex) =>
        new(ConnectionFailedMessage, ex);

    private void ApplyTopK(Dictionary<string, object?> body, int? topK = null)
    {
        var k = topK ?? _opt.TopK;
        if (k > 0)
            body["top_k"] = k;
    }

    /// <summary>
    /// 小型モデルが同じ語句を繰り返し続けるループ（「とか、とか、…」等）の抑止。
    /// repeat_penalty は控えめにして日本語の助詞への影響を避け、
    /// フレーズ単位の反復には DRY サンプラーを主軸にする。
    /// 未対応の古い llama-server では未知フィールドとして無視される。
    /// </summary>
    private static void ApplyRepetitionGuard(Dictionary<string, object?> body)
    {
        body["repeat_penalty"] = 1.08;
        body["repeat_last_n"] = 256;
        body["dry_multiplier"] = 0.7;
        body["dry_base"] = 1.75;
        body["dry_allowed_length"] = 3;
    }

    private static object BuildMessage(ChatTurn m)
    {
        if (m.ImagesBase64 is not { Length: > 0 })
            return new { role = m.Role, content = m.Content };

        // Gemma 4 等: 画像・音声はテキストより前に置くと安定しやすい（Unsloth / Google 推奨）
        var parts = new List<object>();
        foreach (var b64 in m.ImagesBase64)
        {
            if (string.IsNullOrWhiteSpace(b64)) continue;
            var data = b64.Trim();
            if (!data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                data = "data:image/jpeg;base64," + data;
            parts.Add(new { type = "image_url", image_url = new { url = data } });
        }

        if (!string.IsNullOrWhiteSpace(m.Content))
            parts.Add(new { type = "text", text = m.Content });

        if (parts.Count == 0)
            return new { role = m.Role, content = m.Content };

        return new { role = m.Role, content = parts };
    }
}
