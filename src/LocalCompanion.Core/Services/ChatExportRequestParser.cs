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

    private static readonly Regex LooseExtension = new(
        @"(?<![\w./\\])(txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg)(?![\w])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTail = new(
        @"[、,]?\s*(?:(?:結果|内容|まとめ|レポート|報告)(?:を|の)?)?\s*(?:デスクトップ|desktop)(?:上|に)?(?:へ|に)?(?:[^\n。!?]{0,80}?(?:置いといて|置いておいて|置いて|保存して|書いといて|書き出して|出力して|残して|おいて|作って|書いて|ください|お願い))[^\n。!?]{0,40}?(?:\.(?:txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg)|txt|md)?(?:形式|ファイル)?(?:で)?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailFile = new(
        @"[、,]?\s*(?:(?:結果|内容|まとめ|レポート|報告)(?:を|の)?)?\s*(?:テキスト|text)?\s*ファイル(?:に|へ|として|で)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出|書いて|作って|残して))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailLoose = new(
        @"[、,]?\s*(?:(?:結果|内容|まとめ)(?:を|の)?)?\s*(?:(?:txt|md|markdown|テキスト)[^\n。!?]{0,20}?(?:で|に|として))?(?:[^\n。!?]{0,30}?(?:書いておいて|書いといて|作っておいて|吐き出して|出力しておいて|保存しておいて))[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailEn = new(
        @"[,.]?\s*(?:and\s+)?(?:save|export|write)\s+(?:it\s+)?(?:to\s+)?(?:the\s+)?desktop(?:\s+as\s+(?:a\s+)?(?:text\s+)?file)?(?:\s+as\s+\.(?:txt|md|markdown|mdx|rst|csv|json|xml|html?|ya?ml|log|ini|cfg))?\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTextFile = new(
        @"[、,]?\s*(?:テキスト|text)\s*ファイル(?:として|で)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WindowsAbsolutePath = new(
        @"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*)",
        RegexOptions.Compiled);

    private static readonly Regex UncPath = new(
        @"\\\\[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*",
        RegexOptions.Compiled);

    private static readonly Regex RelativeExportPath = new(
        @"\.\\(?:[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*)",
        RegexOptions.Compiled);

    private static readonly Regex DriveLetterRoot = new(
        @"(?<![A-Za-z0-9])[A-Za-z]:\\?(?=\s*(?:に|へ|の中|ドライブ|へ保存|に保存|[、。!?]|$))",
        RegexOptions.Compiled);

    private static readonly Regex StripTailPath = new(
        @"[、,]?\s*(?:[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*)|\\\\[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*|\.\\(?:[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)*))(?:の中)?(?:に|へ)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出|書いて|残して|おいて))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailUsb = new(
        @"[、,]?\s*(?:USB(?:メモリ)?|usb(?:メモリ)?|外付け(?:メモリ|ドライブ)?|リムーバブル(?:ディスク|ドライブ)?|removable(?:\s+storage)?|flash\s+drive)(?:の中)?(?:に|へ)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出|書いて|残して))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StripTailSpecialFolder = new(
        @"[、,]?\s*(?:ドキュメント(?:フォルダ)?|書類フォルダ|downloads?|ダウンロード(?:フォルダ)?|data(?:フォルダ)?|データフォルダー?|ユーザーデータ|アプリ(?:の)?(?:フォルダ|ディレクトリ)|exe(?:の)?横|インストール(?:先|フォルダ)|カレント(?:ディレクトリ)?)(?:に|へ|の中に)?(?:[^\n。!?]{0,40}?(?:保存|置|出力|書き出|書いて|残して))?[。!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string message, out ChatExportRequest request)
    {
        request = null!;
        var trimmed = message.Trim();
        if (trimmed.Length < 6)
            return false;

        if (!LooksLikeExportIntent(trimmed))
            return false;

        request = BuildRequest(trimmed);
        StartupLog.Write(
            $"Chat export intent: target={request.Target.Kind}, ext={request.Extension}, query={TruncateLog(request.Query)}");
        return true;
    }

    /// <summary>「今の処理をもう一度」など、直前の保存付き依頼を繰り返す意図を検出する。</summary>
    public static bool TryInheritRepeatExport(
        string message,
        IReadOnlyList<string> priorUserMessagesNewestFirst,
        out ChatExportRequest request)
    {
        request = null!;
        if (!LooksLikeRepeatRequest(message.Trim()))
            return false;

        foreach (var prior in priorUserMessagesNewestFirst)
        {
            if (!TryParse(prior, out var priorExport))
                continue;

            request = priorExport;
            StartupLog.Write(
                $"Chat export repeat: target={request.Target.Kind}, ext={request.Extension}, query={TruncateLog(request.Query)}");
            return true;
        }

        return false;
    }

    private static ChatExportRequest BuildRequest(string trimmed)
    {
        var extension = DetectExtension(trimmed);
        var fileStem = DetectFileNameStem(trimmed, extension);
        var target = DetectTarget(trimmed);
        var query = StripExportClauses(trimmed);
        if (string.IsNullOrWhiteSpace(query))
            query = trimmed;

        return new ChatExportRequest(
            query.Trim(),
            fileStem,
            ChatTextExportFormats.NormalizeExtension(extension),
            target);
    }

    private static ChatExportTarget DetectTarget(string message)
    {
        var explicitDir = TryExtractExplicitDirectory(message);
        if (explicitDir is not null)
            return new ChatExportTarget(ChatExportTargetKind.Directory, explicitDir);

        if (ContainsUsbCue(message))
            return new ChatExportTarget(ChatExportTargetKind.RemovableStorage);

        if (ContainsDocumentsCue(message))
            return new ChatExportTarget(ChatExportTargetKind.Documents);

        if (ContainsDownloadsCue(message))
            return new ChatExportTarget(ChatExportTargetKind.Downloads);

        if (ContainsUserDataCue(message))
            return new ChatExportTarget(ChatExportTargetKind.UserData);

        if (ContainsAppRootCue(message))
            return new ChatExportTarget(ChatExportTargetKind.AppRoot);

        return new ChatExportTarget(ChatExportTargetKind.Desktop);
    }

    internal static string? TryExtractExplicitDirectory(string message)
    {
        string? best = null;
        foreach (Match match in WindowsAbsolutePath.Matches(message))
        {
            var candidate = NormalizeExtractedPath(match.Value);
            if (best is null || candidate.Length > best.Length)
                best = candidate;
        }

        foreach (Match match in UncPath.Matches(message))
        {
            var candidate = NormalizeExtractedPath(match.Value);
            if (best is null || candidate.Length > best.Length)
                best = candidate;
        }

        foreach (Match match in RelativeExportPath.Matches(message))
        {
            var relative = NormalizeExtractedPath(match.Value);
            var candidate = Path.GetFullPath(Path.Combine(AppPaths.Current.Root, relative));
            if (best is null || candidate.Length > best.Length)
                best = candidate;
        }

        if (best is null)
        {
            var driveRoot = DriveLetterRoot.Match(message);
            if (driveRoot.Success)
            {
                var letter = driveRoot.Value.TrimEnd('\\');
                best = letter.EndsWith(":", StringComparison.Ordinal)
                    ? letter + "\\"
                    : letter;
            }
        }

        if (best is null)
            return null;

        if (ChatTextExportFormats.IsAllowed(Path.GetExtension(best)))
            best = Path.GetDirectoryName(best) ?? best;

        return string.IsNullOrWhiteSpace(best) ? null : best;
    }

    private static string NormalizeExtractedPath(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('」', '』', '"', '\'');
        while (trimmed.Length > 0)
        {
            var last = trimmed[^1];
            if ("にへの、,。！？!?.".Contains(last))
                trimmed = trimmed[..^1].TrimEnd();
            else
                break;
        }

        return trimmed;
    }

    private static bool ContainsUsbCue(string message) =>
        message.Contains("USB", StringComparison.OrdinalIgnoreCase)
        || message.Contains("usbメモリ", StringComparison.OrdinalIgnoreCase)
        || message.Contains("USBメモリ", StringComparison.Ordinal)
        || message.Contains("外付け", StringComparison.Ordinal)
        || message.Contains("リムーバブル", StringComparison.Ordinal)
        || message.Contains("removable", StringComparison.OrdinalIgnoreCase)
        || message.Contains("flash drive", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDocumentsCue(string message) =>
        message.Contains("ドキュメント", StringComparison.Ordinal)
        || message.Contains("書類フォルダ", StringComparison.Ordinal)
        || message.Contains("documents folder", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDownloadsCue(string message) =>
        message.Contains("ダウンロード", StringComparison.Ordinal)
        || message.Contains("download", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUserDataCue(string message) =>
        message.Contains("データフォルダ", StringComparison.Ordinal)
        || message.Contains("dataフォルダ", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ユーザーデータ", StringComparison.Ordinal)
        || message.Contains("user data", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAppRootCue(string message) =>
        message.Contains("アプリのフォルダ", StringComparison.Ordinal)
        || message.Contains("アプリフォルダ", StringComparison.Ordinal)
        || message.Contains("exeの横", StringComparison.OrdinalIgnoreCase)
        || message.Contains("インストール先", StringComparison.Ordinal)
        || message.Contains("インストールフォルダ", StringComparison.Ordinal)
        || message.Contains("カレントディレクトリ", StringComparison.Ordinal)
        || message.Contains("カレント", StringComparison.Ordinal)
        || message.Contains("app folder", StringComparison.OrdinalIgnoreCase)
        || message.Contains("install folder", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeRepeatRequest(string message)
    {
        if (message.Contains("今の処理", StringComparison.Ordinal)
            || message.Contains("さっきの処理", StringComparison.Ordinal)
            || message.Contains("前の処理", StringComparison.Ordinal)
            || message.Contains("再処理", StringComparison.Ordinal)
            || message.Contains("前と同じ", StringComparison.Ordinal)
            || message.Contains("同じことを", StringComparison.Ordinal))
            return true;

        var hasRepeatCue = message.Contains("もう一度", StringComparison.Ordinal)
            || message.Contains("再度", StringComparison.Ordinal)
            || message.Contains("もう一回", StringComparison.Ordinal)
            || message.Contains("もう1回", StringComparison.Ordinal)
            || message.Contains("again", StringComparison.OrdinalIgnoreCase)
            || message.Contains("repeat", StringComparison.OrdinalIgnoreCase)
            || message.Contains("redo", StringComparison.OrdinalIgnoreCase);

        if (!hasRepeatCue)
            return false;

        return message.Contains("処理", StringComparison.Ordinal)
            || message.Contains("お願い", StringComparison.Ordinal)
            || message.Contains("保存", StringComparison.Ordinal)
            || message.Contains("出力", StringComparison.Ordinal)
            || message.Contains("デスクトップ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ファイル", StringComparison.Ordinal)
            || message.Contains("付き合", StringComparison.Ordinal)
            || message.Contains("テスト", StringComparison.Ordinal);
    }

    private static bool LooksLikeExportIntent(string message)
    {
        if (TryExtractExplicitDirectory(message) is not null && ContainsSaveCue(message))
            return true;

        if (ContainsUsbCue(message) && ContainsSaveCue(message))
            return true;

        if (ContainsDocumentsCue(message) && ContainsSaveCue(message))
            return true;

        if (ContainsDownloadsCue(message) && ContainsSaveCue(message))
            return true;

        if (ContainsUserDataCue(message) && ContainsSaveCue(message))
            return true;

        if (ContainsAppRootCue(message) && ContainsSaveCue(message))
            return true;

        if (message.Contains("デスクトップ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsSaveCue(message))
                return true;
        }

        if (message.Contains("ファイル", StringComparison.OrdinalIgnoreCase)
            && ContainsSaveCue(message))
            return true;

        if ((message.Contains("テキストファイル", StringComparison.OrdinalIgnoreCase)
             || message.Contains("text file", StringComparison.OrdinalIgnoreCase))
            && ContainsSaveCue(message))
            return true;

        if ((ExplicitExtension.IsMatch(message) || LooseExtension.IsMatch(message))
            && ContainsSaveCue(message))
            return true;

        if (ContainsLooseExportCue(message))
            return true;

        return false;
    }

    private static bool ContainsLooseExportCue(string message) =>
        (message.Contains("書いてお", StringComparison.Ordinal)
         || message.Contains("書いと", StringComparison.Ordinal)
         || message.Contains("作ってお", StringComparison.Ordinal)
         || message.Contains("吐き出", StringComparison.Ordinal)
         || message.Contains("出力してお", StringComparison.Ordinal))
        && (message.Contains("txt", StringComparison.OrdinalIgnoreCase)
            || message.Contains("md", StringComparison.OrdinalIgnoreCase)
            || message.Contains("テキスト", StringComparison.Ordinal)
            || message.Contains("ファイル", StringComparison.Ordinal));

    private static bool ContainsSaveCue(string message) =>
        message.Contains("置いと", StringComparison.Ordinal)
        || message.Contains("置いて", StringComparison.Ordinal)
        || message.Contains("保存", StringComparison.Ordinal)
        || message.Contains("書き出", StringComparison.Ordinal)
        || message.Contains("書いと", StringComparison.Ordinal)
        || message.Contains("書いて", StringComparison.Ordinal)
        || message.Contains("作って", StringComparison.Ordinal)
        || message.Contains("吐き出", StringComparison.Ordinal)
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
            || message.Contains("text file", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("テキスト", StringComparison.Ordinal) && message.Contains("ファイル", StringComparison.Ordinal)))
            return ".txt";

        var extMatch = ExplicitExtension.Match(message);
        if (extMatch.Success)
            return "." + extMatch.Groups[1].Value.ToLowerInvariant();

        var loose = LooseExtension.Match(message);
        if (loose.Success)
            return "." + loose.Groups[1].Value.ToLowerInvariant();

        if (message.Contains("txt", StringComparison.OrdinalIgnoreCase))
            return ".txt";

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
        for (var i = 0; i < 6; i++)
        {
            var next = StripTail.Replace(q, "");
            next = StripTailPath.Replace(next, "");
            next = StripTailUsb.Replace(next, "");
            next = StripTailSpecialFolder.Replace(next, "");
            next = StripTailFile.Replace(next, "");
            next = StripTailLoose.Replace(next, "");
            next = StripTailEn.Replace(next, "");
            next = StripTextFile.Replace(next, "");
            next = next.Trim().TrimEnd('、', ',', '。', '.', '!', '?', '！', '？');
            if (next == q)
                break;
            q = next;
        }

        return q;
    }

    private static string TruncateLog(string text) =>
        text.Length <= 80 ? text : text[..80] + "…";
}
