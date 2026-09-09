using System.Globalization;
using System.Text.Json;

namespace LocalCompanion.Services.LlamaNative;

/// <summary>llama-server の SSE / JSON から生成 tok/s を拾う。</summary>
internal static class LlamaChatTimings
{
    internal static double? TryReadPredictedPerSecond(JsonElement root)
    {
        JsonElement timings = default;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("timings", out var nested))
            timings = nested;
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("predicted_per_second", out _))
            timings = root;
        else
            return null;

        if (timings.ValueKind != JsonValueKind.Object)
            return null;

        if (timings.TryGetProperty("predicted_per_second", out var pps)
            && TryPositiveDouble(pps, out var direct))
            return direct;

        if (timings.TryGetProperty("predicted_n", out var nEl)
            && timings.TryGetProperty("predicted_ms", out var msEl)
            && TryPositiveDouble(nEl, out var n)
            && TryPositiveDouble(msEl, out var ms))
            return n / (ms / 1000.0);

        return null;
    }

    internal static string FormatTokPerSec(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    private static bool TryPositiveDouble(JsonElement el, out double value)
    {
        value = 0;
        if (!el.TryGetDouble(out var raw) || raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw))
            return false;
        value = raw;
        return true;
    }
}
