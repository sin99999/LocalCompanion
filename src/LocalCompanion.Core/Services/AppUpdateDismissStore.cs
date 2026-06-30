using LocalCompanion.Data;
using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>「このバージョンは後で」とした更新通知の記録。</summary>
public sealed class AppUpdateDismissStore
{
    private readonly string _path;

    public AppUpdateDismissStore(RagDatabase db)
    {
        _path = Path.Combine(db.DataDirectory, "app-update-state.json");
    }

    public bool ShouldOffer(string latestVersion)
    {
        var state = Load();
        return !string.Equals(state.DismissedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldCheckNow(int intervalHours)
    {
        var state = Load();
        if (state.LastCheckedUtc is null)
            return true;

        var interval = TimeSpan.FromHours(Math.Max(1, intervalHours));
        return DateTime.UtcNow - state.LastCheckedUtc.Value >= interval;
    }

    public void MarkChecked()
    {
        var state = Load();
        state.LastCheckedUtc = DateTime.UtcNow;
        Save(state);
    }

    public void DismissVersion(string version)
    {
        var state = Load();
        state.DismissedVersion = version;
        state.LastCheckedUtc = DateTime.UtcNow;
        Save(state);
    }

    private AppUpdateState Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppUpdateState();

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppUpdateState>(json) ?? new AppUpdateState();
        }
        catch
        {
            return new AppUpdateState();
        }
    }

    private void Save(AppUpdateState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(state));
        }
        catch
        {
            /* 記録失敗は起動を妨げない */
        }
    }

    private sealed class AppUpdateState
    {
        public string? DismissedVersion { get; set; }
        public DateTime? LastCheckedUtc { get; set; }
    }
}
