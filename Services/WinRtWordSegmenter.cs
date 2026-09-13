using Windows.Data.Text;

namespace LocalCompanion.Services;

/// <summary>
/// Windows 同梱の <see cref="WordsSegmenter"/>（日本語）で分かち書きする。追加の辞書や NuGet は要らない。
/// 1 文字の語と記号・ひらがなだけの語は落とし、失敗時は Core のヒューリスティックに戻す。
/// </summary>
public sealed class WinRtWordSegmenter : IRagWordSegmenter
{
    private readonly HeuristicWordSegmenter _fallback = new();
    private readonly WordsSegmenter? _segmenter;

    public WinRtWordSegmenter()
    {
        try
        {
            _segmenter = new WordsSegmenter("ja");
        }
        catch
        {
            _segmenter = null;
        }
    }

    public IReadOnlyList<string> Segment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();
        if (_segmenter is null)
            return _fallback.Segment(text);

        try
        {
            var words = new List<string>();
            foreach (var segment in _segmenter.GetTokens(text))
            {
                var word = segment.Text.Trim();
                if (word.Length < 2 || !LooksLikeContentWord(word))
                    continue;
                words.Add(word);
            }

            return words;
        }
        catch
        {
            return _fallback.Segment(text);
        }
    }

    /// <summary>漢字・カタカナ・英数字を 1 文字でも含む語だけ（ひらがなだけの助詞・活用語尾は落とす）。</summary>
    private static bool LooksLikeContentWord(string word)
    {
        foreach (var ch in word)
        {
            if (char.IsLetterOrDigit(ch) && !IsHiragana(ch))
                return true;
        }

        return false;
    }

    private static bool IsHiragana(char ch) => ch is >= '\u3040' and <= '\u309F';
}
