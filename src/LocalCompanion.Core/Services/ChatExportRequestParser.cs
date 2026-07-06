using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>「調べてデスクトップに置いて」系の書き出し意図を検出する。</summary>
internal static class ChatExportRequestParser
{
    private static readonly Regex QuotedFileName = new(
        @"[「""']([^「""']+?\.(?:txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg))[」""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NamedAs = new(
        @"(?:ファイル名|名前)(?:は|を)?[「""']?([^「""'\s、。]+)[」""']?(?:で|として)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitExtension = new(
        @"\.(txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTail = new(
        @"[、,]?\s*(?:(?:結果|内容|まとめ|レポート|報告)(?:を|の)?)?\s*(?:デスクトップ|desktop)(?:上|に)?(?:へ|に)?(?:[^\n。!?]{0,80}?(?:置いといて|置いておいて|置いて|保存して|書いといて|書き出して|出力して|残して|おいて|ください|お願い))[^\n。!?]{0,40}?(?:\.(?:txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg))?(?:形式|ファイル)?(?:で)?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailEn = new(
        @"[,.]?\s*(?:and\s+)?(?:save|export|write)\s+(?:it\s+)?(?:to\s+)?(?:the\s+)?desktop(?:\s+as\s+(?:a\s+)?(?:text\s+)?file)?(?:\s+as\s+\.(?:txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg))?\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTextFile = new(
        @"[、,]?\s*(?:テキスト|text)\s*ファイル(?:として|で)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string message, out ChatExportRequest request)
    {
        request = null!;
        var trimmed = message.Trim();
        if (trimmed.Length < 6)
            return false;

        if (!LooksLikeExportIntent(trimmed))
            return false;

        var extension = DetectExtension(trimmed);
        var fileStem = DetectFileNameStem(trimmed, extension);
        var query = StripExportClauses(trimmed);
        if (string.IsNullOrWhiteSpace(query))
            query = trimmed;

        request = new ChatExportRequest(
            query.Trim(),
            fileStem,
            ChatTextExportFormats.NormalizeExtension(extension),
            ChatExportDestination.Desktop);
        return true;
    }

    private static bool LooksLikeExportIntent(string message)
    {
        if (message.Contains("デスクトップ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsSaveCue(message))
                return true;
        }

        if ((message.Contains("テキストファイル", StringComparison.OrdinalIgnoreCase)
             || message.Contains("text file", StringComparison.OrdinalIgnoreCase))
            && ContainsSaveCue(message))
            return true;

        if (ExplicitExtension.IsMatch(message) && ContainsSaveCue(message))
            return true;

        return false;
    }

    private static bool ContainsSaveCue(string message) =>
        message.Contains("置いと", StringComparison.Ordinal)
        || message.Contains("置いて", StringComparison.Ordinal)
        || message.Contains("保存", StringComparison.Ordinal)
        || message.Contains("書き出", StringComparison.Ordinal)
        || message.Contains("書いと", StringComparison.Ordinal)
        || message.Contains("出力", StringComparison.Ordinal)
        || message.Contains("残して", StringComparison.Ordinal)
        || message.Contains("save to", StringComparison.OrdinalIgnoreCase)
        || message.Contains("export to", StringComparison.OrdinalIgnoreCase)
        || message.Contains("write to", StringComparison.OrdinalIgnoreCase);

    private static string DetectExtension(string message)
    {
        var quoted = QuotedFileName.Match(message);
        if (quoted.Success)
            return Path.GetExtension(quoted.Groups[1].Value);

        if (message.Contains("markdown", StringComparison.OrdinalIgnoreCase)
            || message.Contains("マークダウン", StringComparison.Ordinal))
            return ".md";

        if (message.Contains("csv", StringComparison.OrdinalIgnoreCase))
            return ".csv";

        if (message.Contains("json", StringComparison.OrdinalIgnoreCase))
            return ".json";

        if (message.Contains("yaml", StringComparison.OrdinalIgnoreCase)
            || message.Contains("yml", StringComparison.OrdinalIgnoreCase))
            return ".yaml";

        if (message.Contains("html", StringComparison.OrdinalIgnoreCase))
            return ".html";

        if (message.Contains("xml", StringComparison.OrdinalIgnoreCase))
            return ".xml";

        if (message.Contains("テキストファイル", StringComparison.OrdinalIgnoreCase)
            || message.Contains("text file", StringComparison.OrdinalIgnoreCase))
            return ".txt";

        var extMatch = ExplicitExtension.Match(message);
        if (extMatch.Success)
            return "." + extMatch.Groups[1].Value.ToLowerInvariant();

        return ChatTextExportFormats.DefaultExtension;
    }

    private static string? DetectFileNameStem(string message, string extension)
    {
        var quoted = QuotedFileName.Match(message);
        if (quoted.Success)
            return Path.GetFileNameWithoutExtension(quoted.Groups[1].Value);

        var named = NamedAs.Match(message);
        if (named.Success)
            return Path.GetFileNameWithoutExtension(named.Groups[1].Value);

        return null;
    }

    private static string StripExportClauses(string message)
    {
        var q = message.Trim();
        for (var i = 0; i < 4; i++)
        {
            var next = StripTail.Replace(q, "");
            next = StripTailEn.Replace(next, "");
            next = StripTextFile.Replace(next, "");
            next = next.Trim().TrimEnd('、', ',', '。', '.', '!', '?', '！', '？');
            if (next == q)
                break;
            q = next;
        }

        return q;
    }
}
