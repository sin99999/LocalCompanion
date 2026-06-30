namespace LocalCompanion;

/// <summary>起動進捗ハンドラのスコープ所有（グローバル上書きの干渉を防ぐ）。</summary>
public sealed class StartupProgressScope : IDisposable
{
    private static readonly object Gate = new();
    private static object? _owner;

    private readonly object _scopeOwner;
    private bool _disposed;

    private StartupProgressScope(object owner, Action<StartupProgressReport> handler)
    {
        _scopeOwner = owner;
        lock (Gate)
        {
            _owner = owner;
            StartupProgress.Handler = handler;
        }
    }

    public static StartupProgressScope Acquire(object owner, Action<StartupProgressReport> handler) =>
        new(owner, handler);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (Gate)
        {
            if (ReferenceEquals(_owner, _scopeOwner))
            {
                StartupProgress.Handler = null;
                _owner = null;
            }
        }
    }
}
