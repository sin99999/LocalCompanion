using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>参考資料があるとき、共通指示に何を足すか。</summary>
internal sealed record RagAnswerInstructions(
    /// <summary>法令資料の追加指示を付ける。</summary>
    bool Legal,
    /// <summary>罰則文言の冒頭引用を求める（ヒットに罰則文言がある）。</summary>
    bool QuotePenalty,
    /// <summary>質問された条の範囲に限る（条番号の質問）。</summary>
    bool SingleArticle,
    /// <summary>危険・法令注意（RiskCaution）。</summary>
    bool Risk,
    /// <summary>複数資料の相談（Advisory）。</summary>
    bool Advisory);

/// <summary>
/// 回答ルール表。意図 × 会話モード × 資料の種類 → どの指示を載せるかを 1 か所で決める。
/// キャラクターの有無で指示は変えない（口調は共通指示の「これまでの口調のまま」で保つ）。
/// </summary>
internal static class RagAnswerPolicy
{
    public static RagAnswerInstructions Decide(
        RagQueryPlan plan,
        RagConversationMode mode,
        IReadOnlyList<RagSearchHit> hits)
    {
        var quotePenalty = hits.Any(static h =>
            !string.IsNullOrWhiteSpace(h.PenaltyLead)
            || RagPenaltyTextHelper.ExtractLeadingPenaltySentence(h.PromptText) is not null);

        var legal = quotePenalty
            || plan.Intent is RagQueryIntent.Article or RagQueryIntent.Penalty or RagQueryIntent.Boundary
            || hits.Any(static h => h.ArticleSortKey > 0 || RagConversationGate.IsLegalSource(h));

        return new RagAnswerInstructions(
            Legal: legal,
            QuotePenalty: quotePenalty,
            SingleArticle: plan.Intent == RagQueryIntent.Article,
            Risk: mode == RagConversationMode.RiskCaution,
            Advisory: plan.Intent == RagQueryIntent.Advisory);
    }

    /// <summary>
    /// ヒットが 0 件のときに載せる指示。厳密パス（条文・FAQ・フォーマル照会）だけ「資料なし／検索未完了」を言い、
    /// 雑談寄り（Probe・SoftTopic）は何も載せず普段どおり話す。危険話題は資料が無くても注意だけ付ける。
    /// </summary>
    public static string? ChooseEmptyHitInstruction(RagConversationMode mode, bool searchFailed, bool japanese) =>
        mode switch
        {
            RagConversationMode.Structured => ChatSystemPromptTexts.RagEmptyHitsInstruction(japanese, searchFailed),
            RagConversationMode.RiskCaution => ChatSystemPromptTexts.RagRiskCautionInstruction(japanese),
            _ => null,
        };
}
