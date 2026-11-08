using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class SpeechWavTests
{
    [Fact]
    public async Task WavPcmReader_RoundTripsMono16()
    {
        var pcm = new byte[] { 0, 0, 1, 0, 2, 0, 3, 0 };
        var wav = WavPcmReader.BuildWav(sampleRate: 24000, channels: 1, bitsPerSample: 16, pcm);
        var reader = new WavPcmReader(new MemoryStream(wav));
        var buf = new byte[32];
        var n = await reader.ReadPcmAsync(buf, CancellationToken.None);
        Assert.True(reader.IsPcm16);
        Assert.Equal(24000, reader.SampleRate);
        Assert.Equal(1, reader.Channels);
        Assert.Equal(pcm.Length, n);
        Assert.Equal(pcm, buf[..n]);
        Assert.Equal(0, await reader.ReadPcmAsync(buf, CancellationToken.None));
    }

    [Fact]
    public async Task WavPcmReader_SkipsAChunkBeforeData()
    {
        var pcm = new byte[] { 9, 0, 8, 0 };
        var wav = WavPcmReader.BuildWav(8000, 1, 16, pcm);
        var withPad = new byte[wav.Length + 10];
        Buffer.BlockCopy(wav, 0, withPad, 0, 12);
        withPad[12] = (byte)'J';
        withPad[13] = (byte)'U';
        withPad[14] = (byte)'N';
        withPad[15] = (byte)'K';
        WriteInt(withPad, 16, 2);
        Buffer.BlockCopy(wav, 12, withPad, 22, wav.Length - 12);
        WriteInt(withPad, 4, withPad.Length - 8);

        var reader = new WavPcmReader(new MemoryStream(withPad));
        var buf = new byte[8];
        var n = await reader.ReadPcmAsync(buf, CancellationToken.None);
        Assert.Equal(pcm, buf[..n]);
    }

    [Fact]
    public void Pull_KeepsTheWholeUtteranceUntilTheSentenceEnds()
    {
        var bucket = new PcmBucket();
        bucket.Append(new byte[24000 * 2 + 1000]);
        Assert.Null(SpeechWavPieces.Pull(bucket, sampleRate: 24000, channels: 1, flush: false));
        var whole = SpeechWavPieces.Pull(bucket, 24000, 1, flush: true);
        Assert.NotNull(whole);
        Assert.Equal(44 + 24000 * 2 + 1000, whole!.Length);
        Assert.Null(SpeechWavPieces.Pull(bucket, 24000, 1, flush: true));
    }

    [Theory]
    [InlineData("0.26.0", true)]
    [InlineData("\"0.26.1\"", true)]
    [InlineData("0.26", true)]
    [InlineData("0.25.9", false)]
    [InlineData("1.0.0", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SupportsStreamingVersion_StartsAt026(string? version, bool expected)
    {
        Assert.Equal(expected, VoicevoxClient.SupportsStreamingVersion(version));
    }

    private static void WriteInt(byte[] wav, int offset, int value)
    {
        wav[offset] = (byte)value;
        wav[offset + 1] = (byte)(value >> 8);
        wav[offset + 2] = (byte)(value >> 16);
        wav[offset + 3] = (byte)(value >> 24);
    }
}
