using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>MCP の stdio（1行1JSON）を、say の1件に落とします。入出力そのものは持ちません。</summary>
public static class McpStdioSession
{
    public const string ToolName = "say";

    private static readonly Regex ProtocolVersion = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);

    public static async Task<string?> HandleLineAsync(
        string line,
        Func<string, CancellationToken, Task<string>> say,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        if (root is not JsonObject obj)
            return Error(null, -32600, "Invalid request");

        var method = obj["method"]?.GetValue<string>() ?? "";
        var id = obj["id"];
        if (id is null)
        {
            if (method.StartsWith("notifications/", StringComparison.Ordinal))
                return null;
            return Error(null, -32600, "Invalid request");
        }

        if (method == "initialize")
        {
            var result = new JsonObject
            {
                ["protocolVersion"] = ProtocolOf(obj),
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "local-companion",
                    ["version"] = "1.3.1",
                },
            };
            return Ok(id, result);
        }

        if (method == "ping")
            return Ok(id, new JsonObject());

        if (method == "tools/list")
        {
            var tool = new JsonObject
            {
                ["name"] = ToolName,
                ["description"] = "Send one message to the LocalCompanion window that is already open on this PC, and return its reply. The app must be running and the MCP switch in Settings must be on. This does not fetch web pages, write files, change character settings, or save long-term memories.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["message"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The message to send.",
                        },
                    },
                    ["required"] = new JsonArray("message"),
                },
            };
            return Ok(id, new JsonObject { ["tools"] = new JsonArray(tool) });
        }

        if (method == "tools/call")
            return await CallToolAsync(id, obj["params"] as JsonObject, say, ct);

        return Error(id, -32601, "Method not found");
    }

    private static async Task<string> CallToolAsync(
        JsonNode id,
        JsonObject? parms,
        Func<string, CancellationToken, Task<string>> say,
        CancellationToken ct)
    {
        var name = parms?["name"]?.GetValue<string>();
        if (!string.Equals(name, ToolName, StringComparison.Ordinal))
            return Error(id, -32602, "Unknown tool");

        var message = parms?["arguments"]?["message"]?.GetValue<string>();
        if (message is null)
            return ToolError(id, "The message is missing.");

        try
        {
            var reply = await say(message, ct);
            return ToolText(id, reply, isError: false);
        }
        catch (McpDoorException ex)
        {
            return ToolText(id, ex.Message, isError: true);
        }
    }

    private static string ProtocolOf(JsonObject request)
    {
        var version = request["params"]?["protocolVersion"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(version) && ProtocolVersion.IsMatch(version))
            return version;
        return "2024-11-05";
    }

    private static string Ok(JsonNode id, JsonNode result)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };
        return obj.ToJsonString();
    }

    private static string Error(JsonNode? id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        return obj.ToJsonString();
    }

    private static string ToolText(JsonNode id, string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                }),
            ["isError"] = isError,
        };
        return Ok(id, result);
    }

    private static string ToolError(JsonNode id, string text) => ToolText(id, text, isError: true);
}
