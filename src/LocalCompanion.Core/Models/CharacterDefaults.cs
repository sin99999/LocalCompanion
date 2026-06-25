namespace LocalCompanion.Models;

/// <summary>キャラ設定フォームの生成パラメータ既定値。</summary>
public static class CharacterDefaults
{
    public const double Temperature = 0.8;
    public const double TopP = 0.95;
    public const int TopK = 64;
    public const int ContextLength = 65536;
    public const int MaxOutputTokens = 4096;

    public const double AppTemperature = Temperature;
    public const double AppTopP = TopP;
    public const int AppTopK = TopK;
    public const int AppContextLength = ContextLength;
    public const int AppMaxOutputTokens = MaxOutputTokens;

    /// <summary>E2B / E4B のコンテキスト上限の目安（128K）。UI ヒント用。</summary>
    public const int Gemma4E2ContextHintMax = 131072;
}
