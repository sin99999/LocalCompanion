using System.Globalization;
using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>
/// Laya の /v1/systemone 応答を読む。低い「はい」だけ、遅い生成を飛ばす。
/// 読めない・途中の確率は、今までの動きに戻す。
/// </summary>
internal static class LayaDecision
{
    /// <summary>これより低い「はい」だけ、Gemma への生成を飛ばす。</summary>
    public const double SkipBelow = 0.35;

    /// <summary>
    /// はい／いいえで断片を飛ばすか。Julia は条文の「後で探す事実か」を 0.02 未満と答えた。
    /// オンにすると、法律の質問文まで消えて保存される。会話の振り分けとは別。
    /// </summary>
    internal static readonly bool UseYesNoSkip = false;

    /// <summary>資料を探す、と言い切ったときだけ、安い語彙検索を埋め込み検索へ上げる。</summary>
    public const double SearchConfidence = 0.70;

    public const int MaxStateChars = 240;

    public static bool ShouldSkip(double? yesProbability)
        => yesProbability is double p && p < SkipBelow;

    public static bool ShouldSearchNotes(LayaChoice? choice)
        => choice is { } picked
           && picked.Label.Equals("search", StringComparison.OrdinalIgnoreCase)
           && picked.Confidence >= SearchConfidence;

    public static string Clip(string state)
    {
        var text = state.Trim();
        if (text.Length <= MaxStateChars)
            return text;
        return text[..MaxStateChars];
    }

    public static string BuildNoul(string questionId, string state, string instructions)
        => JsonSerializer.Serialize(new
        {
            state = Clip(state),
            questions = new Dictionary<string, object>
            {
                [questionId] = new { type = "noul", instructions },
            },
        });

    public static string BuildSearchChoice(string state)
        => JsonSerializer.Serialize(new
        {
            state = Clip(state),
            questions = new Dictionary<string, object>
            {
                ["route"] = new
                {
                    type = "choice",
                    instructions = "Should the assistant look up the user's saved notes before answering?",
                    criteria = new Dictionary<string, string>
                    {
                        ["search"] = "the answer depends on the user's saved wording, numbers, file, note, law, or prompt recipe. A request to draw or write still counts when it asks to follow that saved material.",
                        ["chat"] = "greeting, feeling, small talk, or a request to write or draw that can be done without the user's saved notes.",
                    },
                },
            },
        });

    public static double? ReadNoul(string json, string questionId)
    {
        if (!TryAnswer(json, questionId, out var answer))
            return null;
        if (!answer.TryGetProperty("noul", out var noul) || noul.ValueKind != JsonValueKind.Number)
            return null;
        return noul.GetDouble();
    }

    public static LayaChoice? ReadChoice(string json, string questionId)
    {
        if (!TryAnswer(json, questionId, out var answer))
            return null;
        if (!answer.TryGetProperty("choice", out var choiceEl))
            return null;
        var label = choiceEl.GetString();
        if (string.IsNullOrWhiteSpace(label))
            return null;

        double confidence;
        if (answer.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number)
            confidence = conf.GetDouble();
        else if (!TryProbability(answer, label, out confidence))
            return null;

        return new LayaChoice(label, confidence);
    }

    public static string FormatRoute(LayaChoice? choice)
    {
        if (choice is not { } picked)
            return "decision route unread";
        return "decision route " + picked.Label + " "
            + picked.Confidence.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static bool TryProbability(JsonElement answer, string label, out double probability)
    {
        probability = 0;
        if (!answer.TryGetProperty("probabilities", out var probs))
            return false;
        if (!probs.TryGetProperty(label, out var value) || value.ValueKind != JsonValueKind.Number)
            return false;
        probability = value.GetDouble();
        return true;
    }

    private static bool TryAnswer(string json, string questionId, out JsonElement answer)
    {
        answer = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("answers", out var answers))
                return false;
            if (!answers.TryGetProperty(questionId, out var found))
                return false;
            answer = found.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal readonly record struct LayaChoice(string Label, double Confidence);
