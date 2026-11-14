using LocalCompanion.Services;

namespace LocalCompanion;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (McpStdioProxy.IsRequested(args))
        {
            McpStdioProxy.Run();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
