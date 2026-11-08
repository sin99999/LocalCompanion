namespace LocalCompanion.Services;

/// <summary>
/// 返事の本文が伸びるたびに、読み上げてよい文だけを取り出す。
/// 推論は渡さない。呼び出し側が本文だけを渡します。
/// </summary>
public sealed class ReplySpeechCursor
{
    private string _clean = "";
    private int _emitted;
    private int _spokenChars;
    private bool _closed;

    public ReplySpeechCursor(int maxChunkChars, int maxTotalChars)
    {
        MaxChunkChars = Math.Max(1, maxChunkChars);
        MaxTotalChars = Math.Max(1, maxTotalChars);
    }

    public int MaxChunkChars { get; }
    public int MaxTotalChars { get; }

    public IReadOnlyList<string> Push(string? replySoFar, bool flush)
    {
        if (_closed)
            return Array.Empty<string>();

        Align(CleanForSpeech(replySoFar ?? ""));
        return Take(flush);
    }

    private void Align(string cleaned)
    {
        var spoken = _emitted <= _clean.Length ? _clean[.._emitted] : _clean;
        if (cleaned.StartsWith(spoken, StringComparison.Ordinal))
        {
            _clean = cleaned;
            return;
        }

        if (spoken.Length > 0)
        {
            var at = cleaned.IndexOf(spoken, StringComparison.Ordinal);
            if (at >= 0)
            {
                _clean = cleaned;
                _emitted = at + spoken.Length;
                return;
            }
        }

        _clean = cleaned;
        _emitted = cleaned.Length;
    }

    private List<string> Take(bool flush)
    {
        var list = new List<string>();
        while (!_closed && _emitted < _clean.Length && _spokenChars < MaxTotalChars)
        {
            var pending = _clean[_emitted..];
            var budget = MaxTotalChars - _spokenChars;
            var windowLen = Math.Min(pending.Length, Math.Min(MaxChunkChars, budget));
            if (windowLen <= 0)
                break;

            var window = pending[..windowLen];
            var atEnd = windowLen == pending.Length;
            var sentence = FirstSentenceEnd(window, flush && atEnd);
            int take;
            if (sentence >= 0)
                take = sentence + 1;
            else if (flush && atEnd)
                take = windowLen;
            else if (pending.Length >= MaxChunkChars && windowLen >= MaxChunkChars)
                take = MaxChunkChars;
            else if (windowLen == budget && pending.Length >= budget)
                take = windowLen;
            else
                break;

            if (take <= 0)
                break;

            var piece = pending[..take].Trim();
            _emitted += take;
            while (_emitted < _clean.Length && char.IsWhiteSpace(_clean[_emitted]))
                _emitted++;

            if (!HasSpeech(piece))
                continue;

            if (piece.Length > budget)
            {
                piece = piece[..budget];
                _closed = true;
            }

            _spokenChars += piece.Length;
            list.Add(piece);
            if (_spokenChars >= MaxTotalChars)
                _closed = true;
        }

        return list;
    }

    private static string CleanForSpeech(string text)
    {
        var cut = DropUnclosedFence(text);
        return VoicevoxClient.CleanSpeakText(cut);
    }

    private static string DropUnclosedFence(string text)
    {
        var opens = 0;
        var lastOpen = -1;
        for (var i = 0; i + 2 < text.Length; i++)
        {
            if (text[i] != '`' || text[i + 1] != '`' || text[i + 2] != '`')
                continue;
            if (opens % 2 == 0)
                lastOpen = i;
            opens++;
            i += 2;
        }

        if (opens % 2 == 1 && lastOpen >= 0)
            return text[..lastOpen];
        return text;
    }

    private static int FirstSentenceEnd(string window, bool allowTrailingDigitDot)
    {
        for (var i = 0; i < window.Length; i++)
        {
            var ch = window[i];
            if (ch is '。' or '！' or '？' or '!' or '?')
                return i;
            if (ch != '.')
                continue;

            var digitBefore = i > 0 && char.IsDigit(window[i - 1]);
            var digitAfter = i + 1 < window.Length && char.IsDigit(window[i + 1]);
            if (digitBefore && digitAfter)
                continue;
            if (digitBefore && i + 1 >= window.Length && !allowTrailingDigitDot)
                continue;
            return i;
        }

        return -1;
    }

    private static bool HasSpeech(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                return true;
        }

        return false;
    }
}
