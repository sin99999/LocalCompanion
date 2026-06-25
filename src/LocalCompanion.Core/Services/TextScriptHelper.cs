namespace LocalCompanion.Services;

/// <summary>テキストの主要スクリプト（日本語かどうか）を判定します。</summary>
public static class TextScriptHelper
{
    public static bool LooksJapanese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var japanese = 0;
        var letters = 0;

        foreach (var ch in text)
        {
            if (IsJapaneseScript(ch))
                japanese++;
            else if (char.IsLetter(ch))
                letters++;
        }

        return japanese > 0 && japanese >= letters;
    }

    private static bool IsJapaneseScript(char ch) =>
        ch is >= '\u3040' and <= '\u309F'
            or >= '\u30A0' and <= '\u30FF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\u3400' and <= '\u4DBF';
}
