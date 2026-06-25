using System.Drawing;
using System.Drawing.Text;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

public static class SystemFontCatalog
{
    public const string DefaultChatFontFamily = AppSettingsDto.DefaultChatFontFamily;

    private static readonly string[] PreferredFirst =
    [
        DefaultChatFontFamily,
        "Segoe UI Variable Display",
        "Segoe UI",
        "Yu Gothic UI",
        "Meiryo UI",
        "MS Gothic",
        "MS Mincho",
        "Consolas",
        "Cascadia Mono",
        "Cascadia Code",
    ];

    private static IReadOnlyList<string>? _cache;

    public static IReadOnlyList<string> ListFontFamilies()
    {
        if (_cache is not null)
            return _cache;

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var fonts = new InstalledFontCollection())
        {
            foreach (var family in fonts.Families)
            {
                if (family.IsStyleAvailable(FontStyle.Regular))
                    installed.Add(family.Name);
            }
        }

        var ordered = new List<string>(installed.Count);
        foreach (var preferred in PreferredFirst)
        {
            if (installed.Remove(preferred))
                ordered.Add(preferred);
        }

        ordered.AddRange(installed.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase));
        _cache = ordered;
        return _cache;
    }

    public static string NormalizeSelection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultChatFontFamily;

        var trimmed = value.Trim();
        return ListFontFamilies().FirstOrDefault(f =>
            string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? trimmed;
    }
}
