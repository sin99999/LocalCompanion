using Microsoft.UI.Dispatching;

namespace LocalCompanion.ViewModels;

public partial class ChatPageViewModel
{
    private DispatcherQueue? _uiDispatcher;

    public void BindUiDispatcher(DispatcherQueue dispatcher) => _uiDispatcher = dispatcher;

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher is null || _uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }

    private async Task RunOnUiAsync(Action action)
    {
        if (_uiDispatcher is null || _uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiDispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            action();
            return;
        }

        await tcs.Task;
    }

    private async Task AwaitUiFrameAsync()
    {
        if (_uiDispatcher is null)
        {
            await Task.Delay(1);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiDispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => tcs.TrySetResult(true)))
            tcs.TrySetResult(true);

        await tcs.Task;
    }
}
