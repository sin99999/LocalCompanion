namespace LocalCompanion.Models;

/// <summary>キャラ設定フォームの生成パラメータ既定値（Gemma 4 推奨サンプリングに合わせる）。</summary>
public static class CharacterDefaults
{
    /// <summary>Gemma 4 公式推奨。</summary>
    public const double Temperature = 1.0;
    public const double TopP = 0.95;
    public const int TopK = 64;
    /// <summary>
    /// 実用既定。UI 上限は 256K だが、llama-server は大きすぎる値を 16K 前後に丸めるため、
    /// 初期値は実際に載りやすい長さにする。
    /// </summary>
    public const int ContextLength = 16384;
    public const int MaxOutputTokens = 4096;

    public const double AppTemperature = Temperature;
    public const double AppTopP = TopP;
    public const int AppTopK = TopK;
    public const int AppContextLength = ContextLength;
    public const int AppMaxOutputTokens = MaxOutputTokens;

    /// <summary>E2B / E4B のコンテキスト上限の目安（128K）。UI ヒント用。</summary>
    public const int Gemma4E2ContextHintMax = 131072;
    /// <summary>12B / 31B 系のコンテキスト上限の目安（256K）。</summary>
    public const int Gemma4LargeContextHintMax = 262144;
}
