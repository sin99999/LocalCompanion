using System.Text;

namespace LocalCompanion.Services.DocumentReading;

internal sealed class TextUtf8DocumentReader : IDocumentReader
{
    private readonly HashSet<string> _extensions;

    public TextUtf8DocumentReader(IEnumerable<string> extensions)
    {
        _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Extensions => _extensions;

    public string ReadFromPath(string path) =>
        File.ReadAllText(path, RagTextEncoding.DetectFromFile(path));

    public string ReadFromStream(Stream stream, string fileName)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        return RagTextEncoding.DetectFromBytes(bytes).GetString(bytes);
    }
}

internal static class RagTextEncoding
{
    static RagTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    public static Encoding DetectFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return DetectFromBytes(bytes);
    }

    public static Encoding DetectFromBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // UTF-8 の日本語 .md / .txt が SJIS 風バイト列と誤判定されないよう、有効 UTF-8 を優先する。
        if (IsValidUtf8(bytes))
            return Encoding.UTF8;

        if (LooksLikeShiftJis(bytes))
            return Encoding.GetEncoding(932);

        return Encoding.UTF8;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        if (bytes.Length == 0)
            return true;

        var i = 0;
        while (i < bytes.Length)
        {
            var b0 = bytes[i];
            int needed;
            if (b0 <= 0x7F)
                needed = 0;
            else if (b0 is >= 0xC2 and <= 0xDF)
                needed = 1;
            else if (b0 is >= 0xE0 and <= 0xEF)
                needed = 2;
            else if (b0 is >= 0xF0 and <= 0xF4)
                needed = 3;
            else
                return false;

            if (i + needed >= bytes.Length)
                return false;

            for (var j = 1; j <= needed; j++)
            {
                var b = bytes[i + j];
                if (b is not (>= 0x80 and <= 0xBF))
                    return false;
            }

            i += needed + 1;
        }

        return true;
    }

    private static bool LooksLikeShiftJis(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 4096);
        var sjisPairs = 0;
        for (var i = 0; i < sample - 1; i++)
        {
            var b0 = bytes[i];
            var b1 = bytes[i + 1];
            if (b0 is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC
                && b1 is >= 0x40 and <= 0xFC and not 0x7F)
            {
                sjisPairs++;
                i++;
            }
        }

        return sjisPairs >= 4;
    }
}
