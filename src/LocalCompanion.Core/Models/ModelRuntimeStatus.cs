using LocalCompanion.Localization;

namespace LocalCompanion.Models;

public sealed record ModelRuntimeStatus(
    string? SelectedModelFileName,
    string? LoadedModelFileName,
    bool ModelMismatch,
    string? MmprojWarning = null)
{
    public string? ActiveModelFileName => LoadedModelFileName ?? SelectedModelFileName;

    public string? StatusMessage
    {
        get
        {
            if (ModelMismatch)
            {
                return LocalizationService.Instance.Format(
                    "Health.ModelMismatch",
                    SelectedModelFileName ?? "",
                    LoadedModelFileName ?? "");
            }
            if (!string.IsNullOrWhiteSpace(MmprojWarning))
                return MmprojWarning;
            return null;
        }
    }
}
