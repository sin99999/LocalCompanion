using System.Text;
using System.Text.Json;
using LocalCompanion.Localization;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class McpDoorTests
{
    [Fact]
    public void Refusal_AllowsANormalMessage()
    {
        Assert.Null(McpDoorPolicy.Refusal("hello"));
    }

    [Fact]
    public void Refusal_BlocksEmptyLongExportAndPersona()
    {
        Assert.NotNull(McpDoorPolicy.Refusal("  "));
        Assert.NotNull(McpDoorPolicy.Refusal(new string('a', McpDoorPolicy.MaxMessageChars + 1)));
        Assert.NotNull(McpDoorPolicy.Refusal("内容をファイルに保存して"));
        Assert.NotNull(McpDoorPolicy.Refusal("性格を提案して"));
    }

    [Fact]
    public void SessionId_StaysOnTheDoorPrefix()
    {
        var id = McpDoorPolicy.SessionIdFor("preset.json");
        Assert.StartsWith(McpDoorPolicy.SessionPrefix, id, StringComparison.Ordinal);
        Assert.True(McpDoorPolicy.IsDoorSession(id));
        Assert.False(McpDoorPolicy.IsDoorSession("chat-1"));
    }

    [Fact]
    public void NewRequest_DoesNotOpenTheDoor()
    {
        Assert.False(new ChatRequestDto("hello").LocalDoor);
    }

    [Fact]
    public async Task Initialize_EchoesProtocolAndListsSay()
    {
        var line = await McpStdioSession.HandleLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""",
            (_, _) => Task.FromResult("unused"),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("2025-06-18", doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        var list = await McpStdioSession.HandleLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            (_, _) => Task.FromResult("unused"),
            CancellationToken.None);
        using var tools = JsonDocument.Parse(list);
        Assert.Equal("say", tools.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Notification_HasNoResponse()
    {
        var line = await McpStdioSession.HandleLineAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            (_, _) => Task.FromResult("unused"),
            CancellationToken.None);
        Assert.Null(line);
    }

    [Fact]
    public async Task Say_ReturnsTheReplyAndKeepsErrorsAsToolErrors()
    {
        var ok = await McpStdioSession.HandleLineAsync(
            """{"jsonrpc":"2.0","id":"a","method":"tools/call","params":{"name":"say","arguments":{"message":"hi"}}}""",
            (message, _) => Task.FromResult("reply:" + message),
            CancellationToken.None);
        using var okDoc = JsonDocument.Parse(ok);
        Assert.Equal("reply:hi", okDoc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
        Assert.False(okDoc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var bad = await McpStdioSession.HandleLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"say","arguments":{"message":" "}}}""",
            (_, _) => throw new McpDoorException("The message is empty."),
            CancellationToken.None);
        using var badDoc = JsonDocument.Parse(bad);
        Assert.True(badDoc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Pipe_RoundTrip_StaysOnThisUser()
    {
        var name = "LocalCompanion.Mcp.Test." + Guid.NewGuid().ToString("N");
        await using var host = new McpPipeHost(name);
        host.Start((message, _) => Task.FromResult("echo:" + message));
        var reply = await McpPipeLink.AskAsync(name, "ping", CancellationToken.None);
        Assert.Equal("echo:ping", reply);
    }

    [Fact]
    public async Task StdioProxy_WritesOneJsonLine()
    {
        var json = """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"say","arguments":{"message":"yo"}}}""" + "\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var output = new MemoryStream();
        await McpStdioProxy.RunAsync(input, output, (message, _) => Task.FromResult("ok:" + message), CancellationToken.None);
        output.Position = 0;
        using var reader = new StreamReader(output, Encoding.UTF8);
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("ok:yo", doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Languages_HaveTheMcpCopy()
    {
        var japaneseHint = LocalizationResources.For(AppLanguage.Japanese)["Settings.Mcp.Hint"];
        foreach (var language in AppLanguages.All)
        {
            var table = LocalizationResources.For(language);
            Assert.False(string.IsNullOrWhiteSpace(table["Settings.Mcp.Enabled"]));
            Assert.False(string.IsNullOrWhiteSpace(table["Settings.Mcp.Hint"]));
            Assert.False(string.IsNullOrWhiteSpace(table["Settings.Mcp.Usage"]));
            Assert.Contains("say", table["Settings.Mcp.Usage"], StringComparison.Ordinal);
            Assert.Contains("--mcp-stdio", table["Settings.Mcp.Usage"], StringComparison.Ordinal);
            if (language != AppLanguage.Japanese)
                Assert.NotEqual(japaneseHint, table["Settings.Mcp.Hint"]);
        }
    }
}
