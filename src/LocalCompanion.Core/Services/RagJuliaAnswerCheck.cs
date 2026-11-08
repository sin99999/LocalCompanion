namespace LocalCompanion.Services;

/// <summary>
/// Julia の「この断片は、この質問に答えている？」を、残すか外すかに変える。
/// 読めない（null）ときは残す。断片そのものは消さない。
/// </summary>
internal static class RagJuliaAnswerCheck
{
    /// <summary>これより低い「はい」だけ外す。0.35 は、はっきりいいえに寄ったとき。</summary>
    public const double DropBelow = 0.35;

    /// <summary>返す直前に聞く上限。超えた残りは、聞かないで残す。</summary>
    public const int MaxJudgedHits = 8;

    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(4);

    public static bool Keep(double? yesAnswered) =>
        yesAnswered is null || yesAnswered.Value >= DropBelow;

    /// <summary>
    /// Julia に渡す本文。質問を先に置き、240字で切れても質問が残るようにする。
    /// </summary>
    public static string BuildState(string passage, string question)
    {
        var q = (question ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
        if (q.Length > 80)
            q = q[..80];
        var prefix = "Question: " + q + "\n";
        var room = LayaDecision.MaxStateChars - prefix.Length;
        var body = (passage ?? "").Trim();
        if (room <= 0)
            return prefix;
        if (body.Length > room)
            body = body[..room];
        return prefix + body;
    }

    /// <summary>
    /// yesAnswers はヒットの先頭から同じ順。聞き終わらなかった残りは残す。
    /// 聞いた分が全部落ちたら、元の一覧を返す。
    /// </summary>
    public static IReadOnlyList<T> Apply<T>(IReadOnlyList<T> hits, IReadOnlyList<double?> yesAnswers)
    {
        if (hits.Count == 0 || yesAnswers.Count == 0)
            return hits;

        var kept = new List<T>(hits.Count);
        var judged = Math.Min(hits.Count, yesAnswers.Count);
        for (var i = 0; i < judged; i++)
        {
            if (Keep(yesAnswers[i]))
                kept.Add(hits[i]);
        }

        for (var i = judged; i < hits.Count; i++)
            kept.Add(hits[i]);

        return kept.Count == 0 ? hits : kept;
    }
}
