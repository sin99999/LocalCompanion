using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>将来の多段ツール呼び出し用の最小契約（現状はアプリ側オーケストレーションが主体）。</summary>
internal interface IChatAgentTool
{
    string Name { get; }
    Task<ChatAgentToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct);
}

internal sealed record ChatAgentToolResult(bool Ok, string Content);

internal sealed class ChatAgentFetchUrlTool : IChatAgentTool
{
    public string Name => "fetch_url";

    public async Task<ChatAgentToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!args.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return new ChatAgentToolResult(false, "url is required");

        try
        {
            var (name, text) = await ChatUrlContentFetcher.FetchAsync(url, ct);
            return new ChatAgentToolResult(true, $"[{name}]\n{text}");
        }
        catch (Exception ex)
        {
            return new ChatAgentToolResult(false, ex.Message);
        }
    }
}

internal sealed class ChatAgentWebSearchTool : IChatAgentTool
{
    private readonly string? _baseUrl;
    private readonly int _topK;

    public ChatAgentWebSearchTool(string? baseUrl, int topK)
    {
        _baseUrl = baseUrl;
        _topK = topK;
    }

    public string Name => "web_search";

    public async Task<ChatAgentToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return new ChatAgentToolResult(false, "query is required");

        try
        {
            var hits = await ChatWebSearchClient.SearchAsync(query, _baseUrl, _topK, ct);
            if (hits.Count == 0)
                return new ChatAgentToolResult(true, "no hits");

            var lines = hits.Select((h, i) => $"{i + 1}. {h.Title}\n{h.Url}\n{h.Snippet}");
            return new ChatAgentToolResult(true, string.Join("\n\n", lines));
        }
        catch (Exception ex)
        {
            return new ChatAgentToolResult(false, ex.Message);
        }
    }
}

/// <summary>モデルが出す簡易ツール呼び出しブロックをパースする（任意・将来拡張）。</summary>
internal static class ChatAgentToolCallParser
{
    public static bool TryParse(string? text, out string toolName, out Dictionary<string, string> args)
    {
        toolName = "";
        args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // ```tool
        // {"name":"web_search","args":{"query":"..."}}
        // ```
        var start = text.IndexOf("```tool", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;
        var jsonStart = text.IndexOf('{', start);
        var jsonEnd = text.IndexOf("```", jsonStart >= 0 ? jsonStart : start + 7, StringComparison.Ordinal);
        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            return false;

        var json = text[jsonStart..jsonEnd].Trim();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var nameEl))
                return false;
            toolName = nameEl.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsEl.EnumerateObject())
                    args[prop.Name] = prop.Value.ToString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
