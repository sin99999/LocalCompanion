namespace LocalCompanion.Services.LlamaNative;

/// <summary>
/// llama-server に渡すコンテキスト長の上限ポリシー（唯一の定義箇所）。
/// UI・ChatService の履歴予算・サーバー起動引数はすべてここを参照する。
/// </summary>
public static class LlamaContextPolicy
{
    /// <summary>この値を超える設定は <see cref="StandardCap"/> に丸める。</summary>
    public const int HighContextThreshold = 24576;

    /// <summary>大きすぎる設定値に対する実際の上限。</summary>
    public const int StandardCap = 16384;

    /// <summary>大型マルチモーダル（10GB 以上 + mmproj）時の上限。</summary>
    public const int LargeMultimodalCap = 12288;

    /// <summary>大型モデルとみなすファイルサイズ（GB）。</summary>
    public const double LargeModelGbThreshold = 10;

    /// <summary>モデル情報なしで適用できる共通キャップ（サーバー起動・履歴予算用）。</summary>
    public static int CapForServer(int requested) =>
        requested > HighContextThreshold ? StandardCap : requested;

    /// <summary>モデルサイズ・mmproj の有無まで考慮した最終キャップ。</summary>
    public static int CapForModel(int requested, double modelSizeGb, bool hasMmproj)
    {
        var context = CapForServer(requested);
        if (modelSizeGb >= LargeModelGbThreshold && hasMmproj && context > LargeMultimodalCap)
            context = LargeMultimodalCap;
        return context;
    }
}
