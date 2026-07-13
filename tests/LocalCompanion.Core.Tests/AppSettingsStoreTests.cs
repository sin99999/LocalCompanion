using LocalCompanion.Data;
using LocalCompanion.Models;
using LocalCompanion.Services;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Core.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Load_CorruptJson_ResetsToDefaultsAndWritesBackup()
    {
        // DataDirectory はアプリ Root 配下の相対パスのみ許可される
        var relative = Path.Combine("obj", "lc-test-" + Guid.NewGuid().ToString("N"));
        var db = new RagDatabase(Options.Create(new LlamaOptions
        {
            DataDirectory = relative,
        }));
        var dir = db.DataDirectory;
        try
        {
            var path = Path.Combine(dir, "app-settings.json");
            File.WriteAllText(path, "{ not-json");

            var store = new AppSettingsStore(db);
            var loaded = store.Load();
            Assert.Equal(AppSettingsDto.DefaultChatFontSize, loaded.ChatFontSize);

            var backups = Directory.GetFiles(dir, "app-settings.json.bak-*");
            Assert.NotEmpty(backups);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_ChatSearchEnabledTrue_IsForcedOff()
    {
        var relative = Path.Combine("obj", "lc-test-" + Guid.NewGuid().ToString("N"));
        var db = new RagDatabase(Options.Create(new LlamaOptions
        {
            DataDirectory = relative,
        }));
        var dir = db.DataDirectory;
        try
        {
            var store = new AppSettingsStore(db);
            var saved = store.Save(new AppSettingsDto { ChatSearchEnabled = true });
            Assert.False(saved.ChatSearchEnabled);

            var loaded = store.Load();
            Assert.False(loaded.ChatSearchEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
