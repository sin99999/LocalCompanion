namespace LocalCompanion.Models;

/// <summary>キャラ設定の温度・Top P（UI スライダーと API 保存で共通）。</summary>
public static class CharacterSamplingLimits
{
    public const double TemperatureMin = 0.0;
    public const double TemperatureMax = 2.0;
    public const double TemperatureStep = 0.05;

    public const double TopPMin = 0.05;
    public const double TopPMax = 1.0;
    public const double TopPStep = 0.05;

    /// <summary>0＝llama-server に top_k を送らない。</summary>
    public const int TopKMin = 0;
    public const int TopKMax = 128;
    public const int TopKStep = 1;

    public const int ContextLengthMin = 2048;
    /// <summary>UI・保存で許容する上限（256K）。実際に載る量は VRAM とモデル依存。</summary>
    public const int ContextLengthMax = 262144;
    public const int ContextLengthStep = 1024;

    public const int MaxOutputTokensMin = 128;
    public const int MaxOutputTokensMax = 8192;
    public const int MaxOutputTokensStep = 128;
    public const int MaxOutputTokensDefault = CharacterDefaults.MaxOutputTokens;

    public static CharacterSamplingLimitsDto ToDto() =>
        new(
            new SamplingRangeDto(TemperatureMin, TemperatureMax, TemperatureStep),
            new SamplingRangeDto(TopPMin, TopPMax, TopPStep),
            new SamplingRangeDto(TopKMin, TopKMax, TopKStep),
            new SamplingRangeDto(ContextLengthMin, ContextLengthMax, ContextLengthStep));

    public static CharacterProfileDto Normalize(CharacterProfileDto profile) =>
        profile with
        {
            Temperature = SnapTemperature(profile.Temperature),
            TopP = SnapTopP(profile.TopP),
            TopK = SnapTopK(profile.TopK),
            ContextLength = SnapContextLength(profile.ContextLength),
            MaxOutputTokens = SnapMaxOutputTokens(profile.MaxOutputTokens, profile.ContextLength),
        };

    public static double SnapTemperature(double value) =>
        Snap(value, TemperatureMin, TemperatureMax, TemperatureStep);

    public static double SnapTopP(double value) =>
        Snap(value, TopPMin, TopPMax, TopPStep);

    public static int SnapTopK(int value) =>
        Math.Clamp(value, TopKMin, TopKMax);

    public static int SnapContextLength(int value)
    {
        if (value <= 0)
            return CharacterDefaults.AppContextLength;
        value = Math.Clamp(value, ContextLengthMin, ContextLengthMax);
        var maxSteps = (ContextLengthMax - ContextLengthMin) / ContextLengthStep;
        var steps = (int)Math.Round((value - ContextLengthMin) / (double)ContextLengthStep, MidpointRounding.AwayFromZero);
        steps = Math.Clamp(steps, 0, maxSteps);
        return ContextLengthMin + steps * ContextLengthStep;
    }

    public static int MaxOutputTokensCapForContext(int contextLength)
    {
        var ctx = SnapContextLength(contextLength);
        return Math.Clamp(ctx / 2, MaxOutputTokensMin, MaxOutputTokensMax);
    }

    public static int SnapMaxOutputTokens(int value, int contextLength)
    {
        var cap = MaxOutputTokensCapForContext(contextLength);
        if (value <= 0)
            return Math.Min(MaxOutputTokensDefault, cap);

        value = Math.Clamp(value, MaxOutputTokensMin, cap);
        var maxSteps = (cap - MaxOutputTokensMin) / MaxOutputTokensStep;
        var steps = (int)Math.Round((value - MaxOutputTokensMin) / (double)MaxOutputTokensStep, MidpointRounding.AwayFromZero);
        steps = Math.Clamp(steps, 0, maxSteps);
        return MaxOutputTokensMin + steps * MaxOutputTokensStep;
    }

    internal static double Snap(double value, double min, double max, double step)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            value = min;

        value = Math.Clamp(value, min, max);
        var maxSteps = (int)Math.Round((max - min) / step, MidpointRounding.AwayFromZero);
        var steps = (int)Math.Round((value - min) / step, MidpointRounding.AwayFromZero);
        steps = Math.Clamp(steps, 0, maxSteps);
        return RoundToStep(min + steps * step);
    }

    private static double RoundToStep(double value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

public sealed record SamplingRangeDto(double Min, double Max, double Step);

public sealed record CharacterSamplingLimitsDto(
    SamplingRangeDto Temperature,
    SamplingRangeDto TopP,
    SamplingRangeDto TopK,
    SamplingRangeDto ContextLength);
