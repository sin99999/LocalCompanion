using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>キャラ自己改善のモデル応答パーサ（JSON 以外は提案なし）。</summary>
public static partial class CharacterSelfImproveParser
{
    public sealed record ParsedProposal(bool Propose, string Reason, string Persona);

    public static ParsedProposal? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var json = ExtractJsonObject(raw.Trim());
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var propose = false;
            if (root.TryGetProperty("propose", out var proposeEl))
            {
                propose = proposeEl.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(proposeEl.GetString(), out var b) && b,
                    JsonValueKind.Number => proposeEl.TryGetInt32(out var n) && n != 0,
                    _ => false,
                };
            }

            if (!propose)
                return new ParsedProposal(false, string.Empty, string.Empty);

            var reason = root.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? (reasonEl.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var persona = root.TryGetProperty("persona", out var personaEl) && personaEl.ValueKind == JsonValueKind.String
                ? (personaEl.GetString() ?? string.Empty).Trim()
                : string.Empty;

            if (reason.Length > CharacterSelfImproveGuard.MaxReasonChars)
                reason = reason[..CharacterSelfImproveGuard.MaxReasonChars].Trim();

            return new ParsedProposal(true, reason, persona);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        var fenced = FenceRegex().Match(text);
        if (fenced.Success)
            text = fenced.Groups[1].Value.Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return text[start..(end + 1)];
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FenceRegex();
}
