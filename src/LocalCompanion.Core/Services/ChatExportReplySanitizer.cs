using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

/// <summary>LLM が返す偽の「保存しました」文言を除去する。</summary>
internal static class ChatExportReplySanitizer
{
    private static readonly Regex FakeSaveLine = new(
        @"^[ \t「『（(]*.*?(?:デスクトップに保存しました|デスクトップへ?保存|デスクトップに書き込み|保存しました|書き出しました|書き込みました|ファイルに保存)[^\n]*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FakeSavedPathLine = new(
        @"^[ \t]*デスクトップに保存しました\s*:\s*[A-Za-z]:\\[^\n]+$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex FakeSavePathLine = new(
        @"^[ \t]*(?:見て(?:みて)?ね|確認してね|どうぞ)[^\n]*(?:デスクトップ|Desktop)[^\n]*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripFakeSaveClaims(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return reply;

        var cleaned = FakeSaveLine.Replace(reply, "");
        cleaned = FakeSavedPathLine.Replace(cleaned, "");
        cleaned = FakeSavePathLine.Replace(cleaned, "");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        return cleaned.Trim();
    }
}
