using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Core.Tests;

public sealed class VoicevoxSettingsStoreTests
{
    [Fact]
    public void Load_CorruptJson_ResetsToDefaultsAndWritesBackup()
    {
        var relative = Path.Combine("obj", "lc-test-vv-" + Guid.NewGuid().ToString("N"));
        var db = new RagDatabase(Options.Create(new LlamaOptions
        {
            DataDirectory = relative,
        }));
        var dir = db.DataDirectory;
        try
        {
            var path = Path.Combine(dir, "voicevox-settings.json");
            File.WriteAllText(path, "{ not-json");

            var store = new VoicevoxSettingsStore(db);
            var loaded = store.Load();
            Assert.False(loaded.Enabled);

            var backups = Directory.GetFiles(dir, "voicevox-settings.json.bak-*");
            Assert.NotEmpty(backups);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
