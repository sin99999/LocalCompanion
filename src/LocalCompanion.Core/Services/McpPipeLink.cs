using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>同じ Windows 利用者だけの名前付きパイプで、1件の文を渡します。</summary>
public static class McpPipeLink
{
    public const string PipeName = "LocalCompanion.Mcp";

    public const int MaxLineBytes = 48_000;

    public static NamedPipeServerStream CreateServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public static async Task ServeOneAsync(
        Stream stream,
        Func<string, CancellationToken, Task<string>> ask,
        CancellationToken ct)
    {
        var line = await ReadLineLimitedAsync(stream, MaxLineBytes, ct);
        PipeReply reply;
        if (string.IsNullOrWhiteSpace(line))
        {
            reply = new PipeReply("The message is empty.");
        }
        else
        {
            PipeAsk? askBody;
            try
            {
                askBody = JsonSerializer.Deserialize<PipeAsk>(line);
            }
            catch (JsonException)
            {
                askBody = null;
            }

            if (string.IsNullOrWhiteSpace(askBody?.Message))
            {
                reply = new PipeReply("The message is empty.");
            }
            else
            {
                try
                {
                    reply = new PipeReply(null, await ask(askBody.Message, ct));
                }
                catch (McpDoorException ex)
                {
                    reply = new PipeReply(ex.Message);
                }
                catch (Exception ex)
                {
                    StartupLog.Write(ex, "McpPipe");
                    reply = new PipeReply("The request failed.");
                }
            }
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply) + "\n");
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<string> AskAsync(string message, CancellationToken ct) =>
        await AskAsync(PipeName, message, ct);

    public static async Task<string> AskAsync(string pipeName, string message, CancellationToken ct)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(2000, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            throw new McpDoorException("LocalCompanion is not running, or the MCP switch is off.");
        }

        var body = JsonSerializer.Serialize(new PipeAsk(message)) + "\n";
        var bytes = Encoding.UTF8.GetBytes(body);
        await client.WriteAsync(bytes, ct);
        await client.FlushAsync(ct);
        var line = await ReadLineLimitedAsync(client, MaxLineBytes, ct);
        PipeReply? reply;
        try
        {
            reply = string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<PipeReply>(line);
        }
        catch (JsonException)
        {
            reply = null;
        }

        if (!string.IsNullOrWhiteSpace(reply?.Error))
            throw new McpDoorException(reply.Error);
        if (reply?.Reply is null)
            throw new McpDoorException("The request failed.");
        return reply.Reply;
    }

    internal static async Task<string?> ReadLineLimitedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var one = new byte[1];
        while (ms.Length < maxBytes)
        {
            var n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0)
                return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());
            if (one[0] == (byte)'\n')
                break;
            if (one[0] != (byte)'\r')
                ms.WriteByte(one[0]);
        }

        if (ms.Length >= maxBytes)
            throw new McpDoorException("The message is too long.");
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

internal sealed record PipeAsk(string Message);

internal sealed record PipeReply(string? Error, string? Reply = null);

public static class McpStdioProxy
{
    public const string Argument = "--mcp-stdio";

    private static readonly JsonSerializerOptions CursorConfigJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

    public static string CursorConfigJson(string? executablePath)
    {
        var path = string.IsNullOrWhiteSpace(executablePath) ? "LocalCompanion.exe" : executablePath;
        var payload = new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                ["LocalCompanion"] = new Dictionary<string, object>
                {
                    ["command"] = path,
                    ["args"] = new[] { Argument },
                },
            },
        };
        return JsonSerializer.Serialize(payload, CursorConfigJsonOptions);
    }

    public static void Run()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        RunAsync(input, output, (message, token) => McpPipeLink.AskAsync(message, token), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task RunAsync(
        Stream input,
        Stream output,
        Func<string, CancellationToken, Task<string>> say,
        CancellationToken ct)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            var response = await McpStdioSession.HandleLineAsync(line, say, ct);
            if (response is null)
                continue;
            await writer.WriteLineAsync(response.AsMemory(), ct);
        }
    }
}
