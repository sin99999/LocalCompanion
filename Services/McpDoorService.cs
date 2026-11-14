using LocalCompanion.ViewModels;

namespace LocalCompanion.Services;

/// <summary>開いているアプリの会話へ、MCP から1件渡します。</summary>
public sealed class McpDoorService : IAsyncDisposable
{
    private readonly ChatService _chat;
    private readonly CharacterPresetService _presets;
    private readonly ChatPageViewModel _chatPage;
    private readonly SemaphoreSlim _one = new(1, 1);
    private McpPipeHost? _host;

    public McpDoorService(ChatService chat, CharacterPresetService presets, ChatPageViewModel chatPage)
    {
        _chat = chat;
        _presets = presets;
        _chatPage = chatPage;
    }

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            _ = StopAsync();
            return;
        }

        if (_host is { IsRunning: true })
            return;

        _host = new McpPipeHost();
        _host.Start(AskAsync);
    }

    public async Task<string> AskAsync(string message, CancellationToken ct)
    {
        var refusal = McpDoorPolicy.Refusal(message);
        if (refusal is not null)
            throw new McpDoorException(refusal);

        if (_chatPage.IsBusy)
            throw new McpDoorException("LocalCompanion is already writing a reply.");

        if (!await _one.WaitAsync(0, ct))
            throw new McpDoorException("LocalCompanion is already answering another MCP message.");

        try
        {
            if (_chatPage.IsBusy)
                throw new McpDoorException("LocalCompanion is already writing a reply.");

            var preset = CharacterPresetService.ResolveSessionPresetKey(_presets.GetActivePresetFileName());
            var sessionId = McpDoorPolicy.SessionIdFor(preset);
            _chat.EnsureDoorSession(sessionId, preset);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var response = await _chat.ChatAsync(
                new ChatRequestDto(
                    message.Trim(),
                    UseRag: true,
                    UseReasoning: false,
                    UseHistory: true,
                    SessionId: sessionId,
                    LocalDoor: true),
                timeout.Token);
            try
            {
                await _chatPage.ShowDoorSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, "MCP window");
            }

            return response.Reply ?? "";
        }
        catch (McpDoorException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new McpDoorException("The reply took too long.");
        }
        catch (InvalidOperationException)
        {
            throw new McpDoorException("The local model is not ready.");
        }
        finally
        {
            _one.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    private async Task StopAsync()
    {
        if (_host is null)
            return;
        var host = _host;
        _host = null;
        await host.DisposeAsync();
    }
}
