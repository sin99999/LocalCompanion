using System.Text.Json;
using LocalCompanion.Data;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();

    public AppSettingsStore(RagDatabase db)
    {
        _path = Path.Combine(db.DataDirectory, "app-settings.json");
    }

    public AppSettingsDto Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return new AppSettingsDto();

            try
            {
                var json = File.ReadAllText(_path);
                var dto = JsonSerializer.Deserialize<AppSettingsDto>(json, JsonOpts) ?? new AppSettingsDto();
                return Normalize(dto);
            }
            catch (Exception ex)
            {
                TryBackupCorruptSettings(ex);
                return new AppSettingsDto();
            }
        }
    }

    public static string ReadThemeModeForStartup() => AppThemeModes.Dark;

    public AppSettingsDto Save(AppSettingsDto dto)
    {
        var normalized = Normalize(dto);
        lock (_lock)
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(normalized, JsonOpts));
        }

        return normalized;
    }

    private void TryBackupCorruptSettings(Exception ex)
    {
        try
        {
            var bak = _path + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            if (File.Exists(_path))
                File.Copy(_path, bak, overwrite: true);
            StartupLog.Write(ex, $"App settings corrupt; reset to defaults. backup={bak}");
        }
        catch (Exception backupEx)
        {
            StartupLog.Write(backupEx, "App settings corrupt; backup failed");
        }
    }

    private static AppSettingsDto Normalize(AppSettingsDto dto)
    {
        var fontFamily = string.IsNullOrWhiteSpace(dto.ChatFontFamily)
            ? AppSettingsDto.DefaultChatFontFamily
            : SystemFontCatalog.NormalizeSelection(dto.ChatFontFamily);

        var userDisplayName = string.IsNullOrWhiteSpace(dto.UserDisplayName)
            ? string.Empty
            : dto.UserDisplayName.Trim();
        if (userDisplayName.Length > 32)
            userDisplayName = userDisplayName[..32];

        return new AppSettingsDto
        {
            ConfirmHistoryDelete = dto.ConfirmHistoryDelete,
            ThemeMode = AppThemeModes.Dark,
            ChatFontFamily = fontFamily,
            ChatFontSize = Math.Clamp(dto.ChatFontSize, 12, 24),
            UserDisplayName = userDisplayName,
            RagUseHtmlMarkdown = dto.RagUseHtmlMarkdown,
            RagUseLlmStructurer = dto.RagUseLlmStructurer,
            RagSaveStructurerCache = dto.RagSaveStructurerCache,
            RagUsePdfLayoutReader = dto.RagUsePdfLayoutReader,
            MemoryEnabled = dto.MemoryEnabled,
            MemoryAutoExtractOnClose = dto.MemoryAutoExtractOnClose,
            CharacterSelfImproveEnabled = dto.CharacterSelfImproveEnabled,
            ChatSearchEnabled = false,
            SpeechInputEnabled = dto.SpeechInputEnabled,
        };
    }
}
