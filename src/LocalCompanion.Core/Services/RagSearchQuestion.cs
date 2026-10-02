using System.Text;

namespace LocalCompanion.Services;

/// <summary>
/// 取り込み時に1本だけ作る検索用の質問。チャットへ出す本文には混ぜない。
/// モデルが「この断片で答えられる」と言い、断片に無い数字を足していなければ残す。
/// </summary>
internal static class RagSearchQuestion
{
    public const int MaxQuestionChars = 80;

    public static string? TryAccept(string passage, string reply)
    {
        if (string.IsNullOrWhiteSpace(passage) || string.IsNullOrWhiteSpace(reply))
            return null;

        if (!TryParse(reply, out var question, out var grounded) || !grounded)
            return null;

        question = question.Trim();
        if (question.Contains('\n') || question.Contains('\r'))
            return null;
        if (question.Length is < 4 or > MaxQuestionChars)
            return null;
        if (question.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || question.Contains("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!question.Contains('?') && !question.Contains('？') && !question.EndsWith('か'))
            return null;
        if (HasNumberMissingFromPassage(question, passage))
            return null;

        return question;
    }

    public static string WithQuestion(string embeddingText, string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return embeddingText;
        return embeddingText + "\n" + question.Trim();
    }

    private static bool TryParse(string reply, out string question, out bool grounded)
    {
        question = "";
        grounded = false;
        var sawQuestion = false;
        var sawGrounded = false;

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#').Trim();
            if (line.StartsWith("QUESTION:", StringComparison.OrdinalIgnoreCase))
            {
                question = line["QUESTION:".Length..].Trim();
                sawQuestion = true;
            }
            else if (line.StartsWith("GROUNDED:", StringComparison.OrdinalIgnoreCase))
            {
                var mark = line["GROUNDED:".Length..].Trim();
                grounded = mark.Equals("YES", StringComparison.OrdinalIgnoreCase);
                sawGrounded = true;
            }
        }

        if (!sawQuestion || !sawGrounded)
            return false;
        if (question.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool HasNumberMissingFromPassage(string question, string passage)
    {
        var passageNumbers = Numbers(passage);
        foreach (var number in Numbers(question))
        {
            if (!passageNumbers.Contains(number))
                return true;
        }

        return false;
    }

    private static HashSet<string> Numbers(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            var digit = DigitValue(ch);
            if (digit >= 0)
            {
                current.Append((char)('0' + digit));
                continue;
            }

            if (current.Length > 0)
            {
                set.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            set.Add(current.ToString());
        return set;
    }

    private static int DigitValue(char ch)
    {
        if (ch is >= '0' and <= '9')
            return ch - '0';
        return ch switch
        {
            '０' => 0,
            '１' => 1,
            '２' => 2,
            '３' => 3,
            '４' => 4,
            '５' => 5,
            '６' => 6,
            '７' => 7,
            '８' => 8,
            '９' => 9,
            _ => -1,
        };
    }
}
