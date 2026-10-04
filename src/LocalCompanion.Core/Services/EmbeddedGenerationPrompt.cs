using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LocalCompanion.Services;

/// <summary>
/// PNG のテキストチャンクと、JPEG にそのまま残っている生成プロンプトを読む。
/// 画素の中に隠した NovelAI の stealth は対象外。
/// </summary>
internal static class EmbeddedGenerationPrompt
{
    public const int MaxChars = 4000;

    public static string? FormatForChat(IReadOnlyList<string>? images)
    {
        if (images is not { Count: > 0 })
            return null;

        var blocks = new List<string>();
        foreach (var image in images)
        {
            var text = Read(image);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            blocks.Add(text.Trim());
        }

        if (blocks.Count == 0)
            return null;

        var body = blocks.Count == 1
            ? blocks[0]
            : string.Join("\n\n", blocks.Select((text, index) => $"画像{index + 1}\n{text}"));
        if (body.Length > MaxChars)
            body = body[..MaxChars];

        return "【画像に保存されていた生成プロンプト】\n"
               + "この文章は、画像を作ったときに指定したプロンプトです。見た目の説明とは別です。"
               + "プロンプトを作り直すときは、この内容を手がかりにしてください。\n\n"
               + body;
    }

    public static string? Read(string? dataUrlOrBase64)
    {
        if (string.IsNullOrWhiteSpace(dataUrlOrBase64))
            return null;
        var bytes = Decode(dataUrlOrBase64);
        if (bytes is null)
            return null;
        if (IsPng(bytes))
            return ReadPng(bytes);
        return ReadLoose(bytes);
    }

    private static byte[]? Decode(string raw)
    {
        var payload = raw.Trim();
        var marker = payload.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            payload = payload[(marker + "base64,".Length)..];
        payload = payload.Trim();
        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8
        && bytes[0] == 0x89
        && bytes[1] == 0x50
        && bytes[2] == 0x4E
        && bytes[3] == 0x47;

    private static string? ReadPng(byte[] bytes)
    {
        var found = new List<(string Key, string Text)>();
        var index = 8;
        while (index + 12 <= bytes.Length)
        {
            var length = ReadBigEndian(bytes, index);
            if (length < 0 || index + 12L + length > bytes.Length)
                break;
            var type = Encoding.ASCII.GetString(bytes, index + 4, 4);
            var dataStart = index + 8;
            var pair = type switch
            {
                "tEXt" => ReadText(bytes, dataStart, length),
                "zTXt" => ReadZtxt(bytes, dataStart, length),
                "iTXt" => ReadItxt(bytes, dataStart, length),
                _ => null,
            };
            if (pair is not null)
                found.Add(pair.Value);
            if (type == "IEND")
                break;
            index += 12 + length;
        }

        return Pick(found);
    }

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24)
        | (bytes[offset + 1] << 16)
        | (bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static (string Key, string Text)? ReadText(byte[] bytes, int start, int length)
    {
        var split = SplitKeyword(bytes, start, length);
        if (split is null)
            return null;
        var (key, textStart) = split.Value;
        return (key, DecodeBytes(bytes.AsSpan(textStart, start + length - textStart).ToArray()));
    }

    private static (string Key, string Text)? ReadZtxt(byte[] bytes, int start, int length)
    {
        var split = SplitKeyword(bytes, start, length);
        if (split is null)
            return null;
        var (key, methodAt) = split.Value;
        if (methodAt >= start + length || bytes[methodAt] != 0)
            return null;
        var text = Inflate(bytes.AsSpan(methodAt + 1, start + length - (methodAt + 1)));
        return text is null ? null : (key, text);
    }

    private static (string Key, string Text)? ReadItxt(byte[] bytes, int start, int length)
    {
        var end = start + length;
        var split = SplitKeyword(bytes, start, length);
        if (split is null || split.Value.TextStart + 2 >= end)
            return null;
        var key = split.Value.Key;
        var compFlag = bytes[split.Value.TextStart];
        var compMethod = bytes[split.Value.TextStart + 1];
        var languageNul = IndexOfNul(bytes, split.Value.TextStart + 2, end - (split.Value.TextStart + 2));
        if (languageNul < 0)
            return null;
        var translatedNul = IndexOfNul(bytes, languageNul + 1, end - (languageNul + 1));
        if (translatedNul < 0)
            return null;
        var textSpan = bytes.AsSpan(translatedNul + 1, end - (translatedNul + 1));
        if (compFlag == 0)
            return (key, DecodeBytes(textSpan.ToArray()));
        if (compFlag != 1 || compMethod != 0)
            return null;
        var inflated = Inflate(textSpan);
        return inflated is null ? null : (key, inflated);
    }

    private static (string Key, int TextStart)? SplitKeyword(byte[] bytes, int start, int length)
    {
        var nul = IndexOfNul(bytes, start, length);
        if (nul <= start)
            return null;
        var key = Encoding.ASCII.GetString(bytes, start, nul - start);
        return (key, nul + 1);
    }

    private static int IndexOfNul(byte[] bytes, int start, int length)
    {
        var end = Math.Min(bytes.Length, start + length);
        for (var i = start; i < end; i++)
        {
            if (bytes[i] == 0)
                return i;
        }

        return -1;
    }

    private static string? Inflate(ReadOnlySpan<byte> compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return DecodeBytes(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string DecodeBytes(byte[] data)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    private static string? Pick(List<(string Key, string Text)> found)
    {
        string[] preferred = ["parameters", "prompt", "comment", "description"];
        foreach (var want in preferred)
        {
            foreach (var (key, text) in found)
            {
                if (!key.Equals(want, StringComparison.OrdinalIgnoreCase))
                    continue;
                var cleaned = Clean(key, text);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    return cleaned;
            }
        }

        return null;
    }

    private static string? Clean(string key, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (key.Equals("prompt", StringComparison.OrdinalIgnoreCase) && text.StartsWith('{'))
            return TextsFromPromptJson(text);
        if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Negative prompt:", StringComparison.Ordinal))
            return TrimGenerationSettings(text);
        return text;
    }

    private static string? TextsFromPromptJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var texts = new List<string>();
            CollectPromptText(doc.RootElement, texts);
            if (texts.Count == 0)
                return null;
            return string.Join("\n", texts.Distinct(StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectPromptText(JsonElement element, List<string> texts)
    {
        if (texts.Count >= 8)
            return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("text")
                    && property.Value.ValueKind == JsonValueKind.String
                    && LooksLikePrompt(property.Value.GetString()))
                {
                    texts.Add(property.Value.GetString()!.Trim());
                    if (texts.Count >= 8)
                        return;
                    continue;
                }

                CollectPromptText(property.Value, texts);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in element.EnumerateArray())
            CollectPromptText(item, texts);
    }

    private static bool LooksLikePrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = value.Trim();
        if (text.Length < 12)
            return false;
        if (text.Contains(','))
            return true;
        return text.Length >= 24 && text.Contains(' ');
    }

    private static string TrimGenerationSettings(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var keep = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("Steps:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Size:", StringComparison.OrdinalIgnoreCase))
                break;
            keep.Add(line);
        }

        return string.Join('\n', keep).Trim();
    }

    private static string? ReadLoose(byte[] bytes)
    {
        var utf8 = SliceAround(bytes, "Negative prompt:"u8, step: 1);
        if (utf8 is not null)
            return TrimGenerationSettings(utf8);
        var marker = Encoding.Unicode.GetBytes("Negative prompt:");
        var utf16 = SliceAround(bytes, marker, step: 2);
        return utf16 is null ? null : TrimGenerationSettings(utf16);
    }

    private static string? SliceAround(byte[] bytes, ReadOnlySpan<byte> marker, int step)
    {
        var index = bytes.AsSpan().IndexOf(marker);
        if (index < 0)
            return null;

        var start = index;
        var walked = 0;
        while (start >= step && walked < 3500)
        {
            if (!IsPromptUnit(bytes, start - step, step))
                break;
            start -= step;
            walked += step;
        }

        var forward = index + marker.Length;
        var forwarded = 0;
        while (forward + step <= bytes.Length && forwarded < 1200)
        {
            if (!IsPromptUnit(bytes, forward, step))
                break;
            forward += step;
            forwarded += step;
        }

        var text = step == 2
            ? Encoding.Unicode.GetString(bytes, start, forward - start)
            : DecodeBytes(bytes.AsSpan(start, forward - start).ToArray());
        text = text.Trim('\0', ' ', '\r', '\n');
        return MostlyText(text) ? text : null;
    }

    private static bool IsPromptUnit(byte[] bytes, int offset, int step)
    {
        if (step == 1)
        {
            var unit = bytes[offset];
            if (unit == 0xFF)
                return false;
            return unit is 0x09 or 0x0A or 0x0D || unit >= 0x20;
        }

        var code = bytes[offset] | (bytes[offset + 1] << 8);
        return code is 0x09 or 0x0A or 0x0D || code >= 0x20;
    }

    private static bool MostlyText(string text)
    {
        if (text.Length < 8)
            return false;
        foreach (var ch in text)
        {
            if (ch is '\n' or '\r' or '\t')
                continue;
            if (char.IsControl(ch))
                return false;
        }

        return true;
    }
}
