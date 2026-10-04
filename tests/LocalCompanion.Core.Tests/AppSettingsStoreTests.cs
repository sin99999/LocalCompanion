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

    [Fact]
    public void Save_CharacterSelfImproveEnabled_RoundTrips()
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
            Assert.False(store.Load().CharacterSelfImproveEnabled);

            var saved = store.Save(new AppSettingsDto { CharacterSelfImproveEnabled = true });
            Assert.True(saved.CharacterSelfImproveEnabled);
            Assert.True(store.Load().CharacterSelfImproveEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_HistoryDigestEnabled_RoundTrips_DefaultTrue()
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
            Assert.True(store.Load().HistoryDigestEnabled);

            var saved = store.Save(new AppSettingsDto { HistoryDigestEnabled = false });
            Assert.False(saved.HistoryDigestEnabled);
            Assert.False(store.Load().HistoryDigestEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_TokenSpeedAndMtpFlags_RoundTrip()
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
            Assert.False(store.Load().ShowChatTokenSpeed);
            Assert.False(store.Load().SpecDraftMtp);

            var saved = store.Save(new AppSettingsDto
            {
                ShowChatTokenSpeed = true,
                SpecDraftMtp = true,
            });
            Assert.True(saved.ShowChatTokenSpeed);
            Assert.True(saved.SpecDraftMtp);
            Assert.True(store.Load().ShowChatTokenSpeed);
            Assert.True(store.Load().SpecDraftMtp);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_LlamaEngineFlags_RoundTripAndNormalize()
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
            Assert.True(store.Load().LlamaCppAutoUpdate);
            Assert.Equal(LlamaGpuBackendPreferences.Auto, store.Load().GpuBackendPreference);

            var saved = store.Save(new AppSettingsDto
            {
                LlamaCppAutoUpdate = false,
                GpuBackendPreference = "VULKAN",
            });
            Assert.False(saved.LlamaCppAutoUpdate);
            Assert.Equal(LlamaGpuBackendPreferences.Vulkan, saved.GpuBackendPreference);
            Assert.False(store.Load().LlamaCppAutoUpdate);
            Assert.Equal(LlamaGpuBackendPreferences.Vulkan, store.Load().GpuBackendPreference);

            var junk = store.Save(new AppSettingsDto { GpuBackendPreference = "cuda" });
            Assert.Equal(LlamaGpuBackendPreferences.Auto, junk.GpuBackendPreference);

            var juliaOff = store.Save(new AppSettingsDto { JuliaDecisionEnabled = false });
            Assert.False(juliaOff.JuliaDecisionEnabled);
            Assert.False(store.Load().JuliaDecisionEnabled);
            File.WriteAllText(
                Path.Combine(dir, "app-settings.json"),
                "{ \"ConfirmHistoryDelete\": true }");
            Assert.True(store.Load().JuliaDecisionEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
