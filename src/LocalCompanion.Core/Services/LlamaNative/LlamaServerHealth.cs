using System.Net.Http.Json;
using System.Text.Json;

namespace LocalCompanion.Services.LlamaNative;

internal static class LlamaServerHealth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    internal static bool IsModelReady(int port)
    {
        try
        {
            var models = Http.GetFromJsonAsync<JsonElement>($"http://127.0.0.1:{port}/v1/models")
                .GetAwaiter().GetResult();
            if (models.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                return true;
        }
        catch
        {
            /* try props */
        }

        try
        {
            var props = Http.GetFromJsonAsync<JsonElement>($"http://127.0.0.1:{port}/props")
                .GetAwaiter().GetResult();
            if (props.TryGetProperty("model_path", out var mp))
            {
                var path = mp.GetString();
                return !string.IsNullOrEmpty(path) && !string.Equals(path, "none", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            /* not ready */
        }

        return false;
    }
}
