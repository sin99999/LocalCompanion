using LocalCompanion.Data;
using LocalCompanion.Localization;
using LocalCompanion.Services;
using LocalCompanion.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalCompanion.Services;

/// <summary>アプリケーション全体のサービス登録。</summary>
public static class AppServices
{
    public static IServiceProvider Provider { get; private set; } = null!;

    public static void Configure()
    {
        AppPaths.Initialize();
        var paths = AppPaths.Current;

        var config = new ConfigurationBuilder()
            .SetBasePath(paths.ContentRoot)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var configuredDataDir = config.GetSection(LlamaOptions.SectionName)["DataDirectory"];
        var userDataDir = AppPaths.ResolveUserDataDirectory(configuredDataDir);
        StartupLog.ConfigureUserDataDirectory(userDataDir);

        var languageStore = new LanguageSettingsStore(userDataDir);
        _ = new LocalizationService(languageStore);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.Configure<LlamaOptions>(config.GetSection(LlamaOptions.SectionName));
        services.Configure<VoicevoxOptions>(config.GetSection(VoicevoxOptions.SectionName));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));
        services.AddHttpClient<LlamaServerClient>();
        services.AddHttpClient<VoicevoxClient>((sp, client) =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VoicevoxOptions>>().Value;
            client.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(opt.SynthesisTimeoutSeconds, opt.ProbeTimeoutSeconds) + 5);
        });

        services.AddSingleton<VoicevoxSettingsStore>();
        services.AddSingleton<VoicevoxInstallLocator>();
        services.AddSingleton<VoicevoxLifecycleService>();
        services.AddSingleton<VoicevoxUpdateService>();
        services.AddSingleton<VoicevoxSpeakerCacheStore>();
        services.AddSingleton<VoicevoxSpeechService>();
        services.AddSingleton<VoicevoxStartupCoordinator>();
        services.AddSingleton(languageStore);
        services.AddSingleton(LocalizationService.Instance);
        services.AddSingleton<RagDatabase>();
        services.AddSingleton<RagService>();
        services.AddSingleton<CharacterPresetService>();
        services.AddSingleton<CharacterRepository>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<LlamaLifecycleService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<RuntimeHealthService>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<AppAppearanceService>();

        services.AddSingleton<ChatPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();

        Provider = services.BuildServiceProvider();
        StartupLog.Write($"AppServices Root={paths.Root}");
    }

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
