namespace LocalCompanion.Services;

/// <summary>届いた PCM を、再生できる WAV の切れ目に分けます。</summary>
internal static class SpeechWavPieces
{
    public static byte[]? Pull(PcmBucket bucket, int sampleRate, int channels, bool flush)
    {
        if (sampleRate < 1 || channels is < 1 or > 2)
            return null;

        var frame = sampleRate * channels * 2;
        var aligned = bucket.Count - (bucket.Count % (channels * 2));
        if (aligned <= 0)
            return null;
        if (!flush && aligned < frame)
            return null;

        var take = flush ? aligned : frame;
        return WavPcmReader.BuildWav(sampleRate, channels, 16, bucket.Take(take));
    }
}

internal sealed class PcmBucket
{
    private byte[] _data = new byte[4096];
    private int _count;

    public int Count => _count;

    public void Append(ReadOnlySpan<byte> src)
    {
        if (src.Length == 0)
            return;
        if (_count + src.Length > _data.Length)
        {
            var next = _data.Length;
            while (next < _count + src.Length)
                next *= 2;
            Array.Resize(ref _data, next);
        }

        src.CopyTo(_data.AsSpan(_count));
        _count += src.Length;
    }

    public byte[] Take(int count)
    {
        if (count < 0 || count > _count)
            throw new ArgumentOutOfRangeException(nameof(count));
        var piece = new byte[count];
        Buffer.BlockCopy(_data, 0, piece, 0, count);
        _count -= count;
        if (_count > 0)
            Buffer.BlockCopy(_data, count, _data, 0, _count);
        return piece;
    }
}
