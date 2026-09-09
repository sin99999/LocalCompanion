namespace LocalCompanion.Services.LlamaNative;

/// <summary>GGUF 名から MTP 対応っぽさを見る。中身の検証はしない。</summary>
internal static class LlamaMtpSupport
{
    internal static bool LooksCapable(string? modelFileName)
    {
        if (string.IsNullOrWhiteSpace(modelFileName))
            return false;

        var name = Path.GetFileNameWithoutExtension(modelFileName.Trim());
        foreach (var part in name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, "mtp", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool ShouldEnable(bool userEnabled, string? modelFileName) =>
        userEnabled && LooksCapable(modelFileName);
}
