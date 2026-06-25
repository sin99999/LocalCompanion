using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

public sealed class RuntimeHealthService
{
    private readonly LlamaServerClient _llama;
    private readonly ModelCatalogService _models;

    public RuntimeHealthService(LlamaServerClient llama, ModelCatalogService models)
    {
        _llama = llama;
        _models = models;
    }

    public async Task<HealthSummary> GetAsync(CancellationToken ct = default)
    {
        var runtime = await _models.GetRuntimeStatusAsync(_llama, ct);
        var ping = await _llama.PingAsync(ct);
        var loc = LocalizationService.Instance;
        var msg = runtime.StatusMessage;
        if (!ping)
            msg = loc.Get("Health.LlamaDisconnected");
        else if (runtime.LoadedModelFileName is null)
            msg = loc.Get("Health.LlamaWaiting");
        else if (string.IsNullOrWhiteSpace(msg))
            msg = loc.Format("Health.Ok", runtime.LoadedModelFileName);

        var mmprojPath = _models.ResolveMmprojPath();
        var hasMmproj = !string.IsNullOrWhiteSpace(mmprojPath) && File.Exists(mmprojPath);
        var hasModel = !string.IsNullOrWhiteSpace(runtime.ActiveModelFileName);
        var mmprojCompatible = string.IsNullOrWhiteSpace(runtime.MmprojWarning);
        var imageAttachEnabled = ping && hasModel && hasMmproj && mmprojCompatible && !runtime.ModelMismatch;
        var imageAttachHint = imageAttachEnabled ? null : loc.Get("Chat.Attachment.VisionDisabled");

        return new HealthSummary(
            ping,
            runtime.LoadedModelFileName,
            runtime.ModelMismatch,
            msg,
            imageAttachEnabled,
            imageAttachHint);
    }

    public sealed record HealthSummary(
        bool LlamaConnected,
        string? LoadedModel,
        bool ModelMismatch,
        string Message,
        bool ImageAttachEnabled,
        string? ImageAttachHint);
}
