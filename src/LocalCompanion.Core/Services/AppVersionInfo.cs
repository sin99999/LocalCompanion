using System.Reflection;

namespace LocalCompanion.Services;

/// <summary>実行中アプリの製品バージョン（csproj の Version）。</summary>
public static class AppVersionInfo
{
    public static string Current()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            var text = plus > 0 ? informational[..plus] : informational;
            return text.TrimStart('v', 'V');
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
