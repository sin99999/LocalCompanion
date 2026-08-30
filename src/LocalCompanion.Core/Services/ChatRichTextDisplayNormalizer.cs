namespace LocalCompanion.Services;

/// <summary>
/// チャット表示用の文字正規化（WebView2）。
/// 絵文字の異体字セレクタや NUL が選択ヒットテストの穴・ネイティブ落ちの誘因になるため除去する。
/// </summary>
public static class ChatRichTextDisplayNormalizer
{
    /// <summary>表示用。コピーされる SelectedText もこの形になる。</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var needsWork = text.IndexOf('\0') >= 0
            || text.IndexOf('\uFE0F') >= 0
            || text.IndexOf('\uFE0E') >= 0;
        if (!needsWork)
            return text;

        // NUL は PowerToys / WinUI #7299 系で単語選択 AV の誘因として報告あり
        return text.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\uFE0F", string.Empty, StringComparison.Ordinal)
            .Replace("\uFE0E", string.Empty, StringComparison.Ordinal);
    }
}
