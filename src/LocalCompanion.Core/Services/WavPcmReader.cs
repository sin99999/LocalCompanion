namespace LocalCompanion.Services;

/// <summary>WAV のヘッダを読んで、16bit PCM だけを順に取り出します。</summary>
internal sealed class WavPcmReader
{
    private readonly Stream _stream;
    private byte[] _raw = new byte[256];
    private int _rawLen;
    private int _pos;
    private bool _headerParsed;
    private bool _failed;
    private int _dataLeft = int.MaxValue;

    public WavPcmReader(Stream stream)
    {
        _stream = stream;
    }

    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }
    public bool IsPcm16 => _headerParsed && !_failed && BitsPerSample == 16 && Channels is 1 or 2;

    public async Task<int> ReadPcmAsync(byte[] buffer, CancellationToken ct)
    {
        if (_failed)
            return 0;
        if (!_headerParsed && !await ParseHeaderAsync(ct))
            return 0;
        if (!IsPcm16 || buffer.Length == 0)
            return 0;

        var filled = 0;
        while (filled < buffer.Length && _dataLeft > 0)
        {
            var have = _rawLen - _pos;
            if (have == 0)
            {
                var want = Math.Min(buffer.Length - filled, _dataLeft);
                var n = await _stream.ReadAsync(buffer.AsMemory(filled, want), ct);
                if (n == 0)
                    break;
                filled += n;
                _dataLeft -= n;
                continue;
            }

            var takeBuffered = Math.Min(Math.Min(have, buffer.Length - filled), _dataLeft);
            Buffer.BlockCopy(_raw, _pos, buffer, filled, takeBuffered);
            _pos += takeBuffered;
            filled += takeBuffered;
            _dataLeft -= takeBuffered;
        }

        return filled;
    }

    public static byte[] BuildWav(int sampleRate, int channels, int bitsPerSample, ReadOnlySpan<byte> pcm)
    {
        var blockAlign = channels * (bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataLen = pcm.Length;
        var wav = new byte[44 + dataLen];
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        WriteInt(wav, 4, 36 + dataLen);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        WriteInt(wav, 16, 16);
        WriteShort(wav, 20, 1);
        WriteShort(wav, 22, (short)channels);
        WriteInt(wav, 24, sampleRate);
        WriteInt(wav, 28, byteRate);
        WriteShort(wav, 32, (short)blockAlign);
        WriteShort(wav, 34, (short)bitsPerSample);
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        WriteInt(wav, 40, dataLen);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private async Task<bool> ParseHeaderAsync(CancellationToken ct)
    {
        _headerParsed = true;
        if (!await EnsureAsync(12, ct) || ReadFour() != "RIFF")
            return Fail();
        _pos += 4;
        if (ReadFour() != "WAVE")
            return Fail();

        var sawFmt = false;
        while (true)
        {
            if (!await EnsureAsync(8, ct))
                return Fail();
            var id = ReadFour();
            var size = ReadInt();
            if (id == "fmt ")
            {
                if (!await EnsureAsync(size, ct) || size < 16)
                    return Fail();
                var format = ReadShort();
                Channels = ReadShort();
                SampleRate = ReadInt();
                _pos += 6; // byte rate と block align
                BitsPerSample = ReadShort();
                var rest = size - 16;
                if (rest > 0)
                    _pos += rest;
                if (size % 2 == 1)
                    _pos++;
                if (format != 1)
                    return Fail();
                sawFmt = true;
                continue;
            }

            if (id == "data")
            {
                if (!sawFmt)
                    return Fail();
                _dataLeft = size < 0 ? int.MaxValue : size;
                return true;
            }

            if (!await EnsureAsync(size + (size % 2), ct))
                return Fail();
            _pos += size + (size % 2);
        }
    }

    private bool Fail()
    {
        _failed = true;
        return false;
    }

    private async Task<bool> EnsureAsync(int count, CancellationToken ct)
    {
        while (_rawLen - _pos < count)
        {
            Compact();
            if (_rawLen + 4096 > _raw.Length)
                Array.Resize(ref _raw, Math.Max(_raw.Length * 2, _rawLen + count));
            var n = await _stream.ReadAsync(_raw.AsMemory(_rawLen), ct);
            if (n == 0)
                return false;
            _rawLen += n;
        }

        return true;
    }

    private void Compact()
    {
        if (_pos <= 0)
            return;
        var keep = _rawLen - _pos;
        Buffer.BlockCopy(_raw, _pos, _raw, 0, keep);
        _rawLen = keep;
        _pos = 0;
    }

    private string ReadFour()
    {
        var s = System.Text.Encoding.ASCII.GetString(_raw, _pos, 4);
        _pos += 4;
        return s;
    }

    private int ReadInt()
    {
        var v = _raw[_pos] | (_raw[_pos + 1] << 8) | (_raw[_pos + 2] << 16) | (_raw[_pos + 3] << 24);
        _pos += 4;
        return v;
    }

    private short ReadShort()
    {
        var v = (short)(_raw[_pos] | (_raw[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    private static void WriteInt(byte[] wav, int offset, int value)
    {
        wav[offset] = (byte)value;
        wav[offset + 1] = (byte)(value >> 8);
        wav[offset + 2] = (byte)(value >> 16);
        wav[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] wav, int offset, short value)
    {
        wav[offset] = (byte)value;
        wav[offset + 1] = (byte)(value >> 8);
    }
}
