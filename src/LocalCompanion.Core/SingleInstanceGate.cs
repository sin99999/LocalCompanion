namespace LocalCompanion;

/// <summary>同一マシンで LocalCompanion を二重起動しない。</summary>
public static class SingleInstanceGate
{
    private static Mutex? _mutex;

    /// <summary>このプロセスが唯一のインスタンスなら true。2 つ目以降は false。</summary>
    public static bool TryEnter()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: @"Local\LocalCompanion_WinUI_SingleInstance", out var createdNew);
            if (createdNew)
                return true;

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch
        {
            // Mutex が使えない環境では起動を優先（終了時の Job Object 等で緩和）
            return true;
        }
    }
}
