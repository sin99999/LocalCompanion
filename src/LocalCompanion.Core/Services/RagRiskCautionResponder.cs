using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 危険話題（RiskCaution）で法令ヒットがあるとき、LLM 合成に頼らず罰則・条文を機械引用する。
/// </summary>
internal static class RagRiskCautionResponder
{
    public static bool TryFormat(
        RagConversationMode mode,
        IReadOnlyList<RagSearchHit> hits,
        string userMessage,
        bool japanese,
        out string reply)
    {
        reply = "";
        if (mode != RagConversationMode.RiskCaution || hits.Count == 0)
            return false;

        var pick = PickBestHit(hits, userMessage);
        if (pick is null)
            return false;

        var (hit, quote) = pick.Value;
        var header = string.IsNullOrWhiteSpace(hit.HeaderText) ? "" : hit.HeaderText.Trim();
        var source = hit.FormatSourceLabel(0);

        reply = japanese
            ? BuildJapanese(header, quote, source)
            : BuildEnglish(header, quote, source);
        return true;
    }

    internal static (RagSearchHit Hit, string Quote)? PickBestHit(
        IReadOnlyList<RagSearchHit> hits,
        string userMessage)
    {
        var query = RagSoftQueryExpander.Expand(userMessage);
        var needles = RagConversationGate.ExtractNeedles(query);

        RagSearchHit? best = null;
        string? bestQuote = null;
        var bestScore = int.MinValue;

        foreach (var hit in hits)
        {
            var family = HeaderMatchesQueryFamily(hit, query);
            var quote = ResolveQuote(hit, family);
            if (string.IsNullOrWhiteSpace(quote))
                continue;

            var overlap = RagSoftHitRanker.CountNeedleHits(hit, needles);
            // 質問語と無関係な法令（万引きのあとの殺人で窃盗が出る等）を落とす
            if (needles.Count > 0 && overlap <= 0 && !family)
                continue;

            var score = 0;
            if (RagConversationGate.IsLegalSource(hit))
                score += 4;
            if (!string.IsNullOrWhiteSpace(hit.PenaltyLead))
                score += 3;
            if (RagPenaltyTextHelper.ExtractLeadingPenaltySentence(hit.PromptText) is not null)
                score += 2;
            score += overlap * 5;
            if (family)
                score += 8;

            if (score > bestScore)
            {
                bestScore = score;
                best = hit;
                bestQuote = quote;
            }
        }

        if (best is null || string.IsNullOrWhiteSpace(bestQuote) || bestScore < 4)
            return null;

        return (best, bestQuote!);
    }

    /// <summary>質問の犯罪家族とヒットの罪名が粗く一致するか。</summary>
    internal static bool HeaderMatchesQueryFamily(RagSearchHit hit, string query)
    {
        var hay = string.Concat(hit.HeaderText, "\n", hit.PromptText, "\n", hit.PenaltyLead);
        var family = DetectCrimeFamily(query);
        return family != CrimeFamily.Unknown && HitMatchesFamily(hay, family);
    }

    internal static CrimeFamily DetectCrimeFamily(string query)
    {
        if (ContainsAny(query, "殺す", "殺し", "殺害", "殺人", "murder", "homicide"))
            return CrimeFamily.Murder;
        if (ContainsAny(query, "万引き", "窃盗", "窃取", "盗む", "盗み", "theft", "steal"))
            return CrimeFamily.Theft;
        if (ContainsAny(query, "略取", "誘拐", "監禁", "kidnap", "abduction"))
            return CrimeFamily.Abduction;
        if (ContainsAny(query, "不同意性交", "強制性交", "わいせつ", "性交"))
            return CrimeFamily.SexualOffense;
        return CrimeFamily.Unknown;
    }

    private static bool HitMatchesFamily(string hay, CrimeFamily family) =>
        family switch
        {
            CrimeFamily.Murder => ContainsAny(hay, "殺人", "殺害", "人を殺"),
            CrimeFamily.Theft => ContainsAny(hay, "窃盗", "窃取"),
            CrimeFamily.Abduction => ContainsAny(hay, "略取", "誘拐", "監禁"),
            CrimeFamily.SexualOffense => ContainsAny(hay, "不同意性交", "強制性交", "わいせつ", "性交"),
            _ => false,
        };

    private static bool ContainsAny(string text, params string[] cues)
    {
        foreach (var cue in cues)
        {
            if (text.Contains(cue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ResolveQuote(RagSearchHit hit, bool familyMatch)
    {
        if (!string.IsNullOrWhiteSpace(hit.PenaltyLead))
            return hit.PenaltyLead.Trim();

        var fromBody = RagPenaltyTextHelper.ExtractLeadingPenaltySentence(hit.PromptText);
        if (!string.IsNullOrWhiteSpace(fromBody))
            return fromBody;

        if (!RagConversationGate.IsLegalSource(hit))
            return null;

        var hay = string.Concat(hit.HeaderText, "\n", hit.PromptText);
        var offenseLike = familyMatch
            || ContainsAny(hay, "窃盗", "窃取", "殺人", "殺害", "略取", "誘拐", "不同意性交", "強制性交", "わいせつ");
        if (!offenseLike)
            return null;

        var text = hit.PromptText.Trim();
        if (text.Length > 280)
            text = text[..280].Trim() + "…";
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string BuildJapanese(string header, string quote, string source)
    {
        var title = string.IsNullOrWhiteSpace(header)
            ? "【資料記載の罰則・条文】"
            : $"【資料記載の罰則・条文】{header}";
        return $"""
            {title}
            {quote}

            出典: {source}

            やり方は教えないよ。捕まるかどうかは状況次第だけど、見つかればこういう罪・罰則になりうる、って資料には書いてある。条文番号や罪名でもう一回聞いてくれたら探しやすいよ。
            """.Trim();
    }

    private static string BuildEnglish(string header, string quote, string source)
    {
        var title = string.IsNullOrWhiteSpace(header)
            ? "[Penalty / statute from materials]"
            : $"[Penalty / statute from materials] {header}";
        return $"""
            {title}
            {quote}

            Source: {source}

            I won't help with how to do it. Whether you're caught depends on the situation, but the materials say it can be treated under penalties like these. Ask again with an article number or offense name if you want a tighter lookup.
            """.Trim();
    }

    internal enum CrimeFamily
    {
        Unknown,
        Murder,
        Theft,
        Abduction,
        SexualOffense,
    }
}
