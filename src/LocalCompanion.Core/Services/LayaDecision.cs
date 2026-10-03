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
                    instructions = "Does this message need a search of the user's saved notes?",
                    criteria = new Dictionary<string, string>
                    {
                        ["search"] = "asks about a saved note, document, fact, law, or something to look up",
                        ["chat"] = "greeting, feeling, small talk, or a request to write or draw",
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
