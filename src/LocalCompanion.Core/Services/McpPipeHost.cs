namespace LocalCompanion.Services;

/// <summary>MCP のパイプを、切るまで受け付けます。</summary>
public sealed class McpPipeHost : IAsyncDisposable
{
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public McpPipeHost(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? McpPipeLink.PipeName : pipeName;
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start(Func<string, CancellationToken, Task<string>> ask)
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => LoopAsync(ask, ct), ct);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        await _cts.CancelAsync();
        try
        {
            if (_loop is not null)
                await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* 停止 */
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task LoopAsync(Func<string, CancellationToken, Task<string>> ask, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var server = McpPipeLink.CreateServer(_pipeName);
            try
            {
                await server.WaitForConnectionAsync(ct);
                await McpPipeLink.ServeOneAsync(server, ask, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, "McpPipe");
            }
            finally
            {
                if (server.IsConnected)
                    server.Disconnect();
            }
        }
    }
}
