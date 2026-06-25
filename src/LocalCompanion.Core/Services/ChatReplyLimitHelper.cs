using System.Text.RegularExpressions;

namespace LocalCompanion.Services;

internal static class ChatReplyLimitHelper
{
    private static readonly Regex LengthAskPattern = new(
        @"([\d０-９]{1,12}|[一二三四五六七八九十百千万億]+)\s*(?:文字|字数|字(?![幕符])|characters?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string LimitNoticeSuffix(int limit, bool japanese = true) =>
        japanese
            ? $"\n\n…（返答は最大{limit:N0}文字までです。より短い指定にしてください）"
            : $"\n\n… (Replies are limited to {limit:N0} characters. Please request a shorter length.)";

    public static string SystemLimitNote(int limit, bool japanese = true) =>
        japanese
            ? $"【返答の長さ】1回の返答は{limit:N0}文字以内に収めてください。超える長さは書かないでください。文字数制限の説明は返答に含めないでください（アプリ側で処理します）。"
            : $"[Reply length] Keep each reply within {limit:N0} characters. Do not exceed it. Do not mention the character limit in your reply (the app handles that).";

    public static string ExcessiveRequestNote(int limit, bool japanese = true) =>
        japanese
            ? $"【注意】ユーザーは{limit:N0}文字を超える長さを求めています。{limit:N0}字以内で答えてください。制限の説明文は返答に含めないでください。"
            : $"[Note] The user requested more than {limit:N0} characters. Reply within {limit:N0} characters. Do not include limit disclaimers in your reply.";

    public static bool UserRequestsExcessiveLength(string message, int limit)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (Regex.IsMatch(message, @"億\s*文字|億字"))
            return true;
        if (Regex.IsMatch(message, @"million\s+character|billion\s+character", RegexOptions.IgnoreCase))
            return true;

        foreach (Match m in LengthAskPattern.Matches(message))
        {
            var n = ParseLooseNumber(m.Groups[1].Value);
            if (n > limit)
                return true;
        }

        return false;
    }

    public static string ApplyLimit(string reply, int limit, bool japanese = true)
    {
        if (string.IsNullOrEmpty(reply) || reply.Length <= limit)
            return reply;

        var suffix = LimitNoticeSuffix(limit, japanese);
        var keep = limit - suffix.Length;
        if (keep < 200)
            keep = limit;

        return reply[..keep] + suffix;
    }

    public static string FinishReply(string reply, int limit, bool hitStreamCap, bool japanese = true)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return reply;

        reply = StripEmbeddedLimitNotice(reply, limit);

        if (reply.Length > limit)
            return ApplyLimit(reply, limit, japanese);

        if (hitStreamCap)
            return reply + LimitNoticeSuffix(limit, japanese);

        return reply;
    }

    /// <summary>モデルがプロンプト指示で付けた制限説明を除去（アプリ側 suffix と二重化しない）。</summary>
    public static string StripEmbeddedLimitNotice(string reply, int limit)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return reply;

        var limitDigits = limit.ToString();
        var limitGrouped = limit.ToString("N0");
        var patterns = new[]
        {
            $@"[\r\n\s→\-・]*返答は最大(?:{limitGrouped}|{limitDigits})文字までです。?\s*それ以下で指定してください。?",
            $@"[\r\n\s→\-・]*…?\s*（返答は最大(?:{limitGrouped}|{limitDigits})文字までです。?より短い指定にしてください）",
            @"[\r\n\s→\-・]*Replies are limited to [\d,]+ characters\.?\s*Please request a shorter length\.?",
        };

        var trimmed = reply;
        foreach (var pattern in patterns)
        {
            trimmed = Regex.Replace(trimmed, pattern, string.Empty, RegexOptions.IgnoreCase);
        }

        return trimmed.TrimEnd();
    }

    private static long ParseLooseNumber(string raw)
    {
        raw = raw.Trim();
        if (long.TryParse(NormalizeDigits(raw), out var n))
            return n;

        return ParseJapaneseNumber(raw);
    }

    private static string NormalizeDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '０' && chars[i] <= '９')
                chars[i] = (char)('0' + (chars[i] - '０'));
        }
        return new string(chars);
    }

    private static long ParseJapaneseNumber(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0;

        long total = 0;
        long current = 0;
        foreach (var ch in s)
        {
            var digit = ch switch
            {
                '一' or '１' => 1,
                '二' or '２' => 2,
                '三' or '３' => 3,
                '四' or '４' => 4,
                '五' or '５' => 5,
                '六' or '６' => 6,
                '七' or '７' => 7,
                '八' or '８' => 8,
                '九' or '９' => 9,
                _ => -1
            };
            if (digit >= 0)
            {
                current = current * 10 + digit;
                continue;
            }

            switch (ch)
            {
                case '十':
                    current = current == 0 ? 10 : current * 10;
                    break;
                case '百':
                    total += (current == 0 ? 1 : current) * 100;
                    current = 0;
                    break;
                case '千':
                    total += (current == 0 ? 1 : current) * 1000;
                    current = 0;
                    break;
                case '万':
                    total += (current == 0 ? 1 : current) * 10_000;
                    current = 0;
                    break;
                case '億':
                    total += (current == 0 ? 1 : current) * 100_000_000;
                    current = 0;
                    break;
            }
        }

        return total + current;
    }
}
