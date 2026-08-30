using System.Text;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>雑談中心アプリ向けの RAG 実行モード。</summary>
internal enum RagConversationMode
{
    /// <summary>検索しない（雑談）。</summary>
    Skip,

    /// <summary>話題に応じて検索し、弱いヒットは捨てる。</summary>
    SoftTopic,

    /// <summary>犯罪・危険行為の気配。法令寄りヒット＋注意口調。</summary>
    RiskCaution,

    /// <summary>条文・FAQ・フォーマル照会など既存の厳密パス。</summary>
    Structured,
}

/// <summary>
/// 雑談では RAG を走らせず、法律・資料・危険話題のときだけ検索する。
/// 弱いヒットは語の重なりで落とす。
/// </summary>
internal static class RagConversationGate
{
    private static readonly string[] CrimeRiskCues =
    [
        "盗む", "盗み", "窃盗", "強盗", "万引き", "空き巣", "横領", "詐欺", "恐喝",
        "殴る", "殴り", "暴行", "傷害", "殺す", "殺し", "殺害", "監禁", "誘拐", "略取",
        "放火", "脅迫", "ストーカー", "盗撮", "わいせつ", "不同意性交", "強制性交", "性交",
        "賄賂", "贈賄", "収賄",
        "脱税", "覚せい剤", "覚醒剤", "大麻", "コカイン", "不正アクセス",
        "闇バイト", "特殊詐欺", "振り込め", "リベンジポルノ", "児童ポルノ",
        "違法", "犯罪", "捕まる", "逮捕", "起訴",
        "steal", "theft", "fraud", "murder", "assault", "bribe", "illegal", "crime",
        "kidnap", "abduction",
    ];

    private static readonly string[] SoftTopicCues =
    [
        "法律", "法令", "条文", "罰則", "刑法", "民法", "刑訴", "労働", "就業",
        "契約", "残業", "有給", "税金", "脱税", "規約", "条例", "資料", "RAG",
        "調べ", "参照", "根拠", "何条", "合法", "違法", "犯罪", "罰", "国外犯",
        "definition", "article", "penalty", "statute", "law ",
    ];

    private static readonly string[] LegalSourceHints =
    [
        "刑法", "刑事", "刑訴", "法令", "罰則", "民法", "労働", "就業", "規約",
        "条例", "税法", "破産", "憲法", "code", "penal", "criminal", "statute",
    ];

    private static readonly HashSet<string> NeedleStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "です", "ます", "でした", "ません", "して", "いる", "から", "って", "さん", "ちゃん",
        "これ", "それ", "あれ", "なに", "何で", "どう", "して", "する", "した", "かも",
        "たい", "けど", "でも", "ので", "よう", "感じ", "思う", "ください", "お願い",
        "the", "and", "you", "are", "is", "was", "for", "with", "this", "that",
    };

    public static RagConversationMode Resolve(RagQueryPlan plan, string userMessage)
    {
        if (plan.Intent is not RagQueryIntent.General)
            return RagConversationMode.Structured;

        if (RagFormalLegalCue.IsFormalLegalQuery(userMessage))
            return RagConversationMode.Structured;

        if (LooksLikeCrimeRisk(userMessage))
            return RagConversationMode.RiskCaution;

        if (LooksLikeSoftTopic(userMessage))
            return RagConversationMode.SoftTopic;

        return RagConversationMode.Skip;
    }

    public static bool LooksLikeCrimeRisk(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var cue in CrimeRiskCues)
        {
            if (message.Contains(cue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool LooksLikeSoftTopic(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var cue in SoftTopicCues)
        {
            if (message.Contains(cue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static RagSearchResult ApplyHitPolicy(
        RagSearchResult result,
        string userMessage,
        RagConversationMode mode)
    {
        if (result.Hits.Count == 0)
            return result;

        if (mode is RagConversationMode.Structured)
            return result;

        IReadOnlyList<RagSearchHit> hits = result.Hits;
        if (mode == RagConversationMode.RiskCaution)
        {
            // 危険話題は法令資料を優先（条文本文に「万引き」等が無くても拾う）
            var legal = hits.Where(IsLegalSource).Take(3).ToList();
            var filterQuery = RagSoftQueryExpander.Expand(userMessage);
            hits = legal.Count > 0 ? legal : FilterWeakHits(hits, filterQuery);
        }
        else if (mode == RagConversationMode.SoftTopic)
        {
            // 残業→労働時間など、本文に出やすい語で床と順位を見る
            var filterQuery = RagSoftQueryExpander.Expand(userMessage);
            hits = FilterWeakHits(hits, filterQuery);
            hits = RagSoftHitRanker.OrderByNeedleOverlap(hits, filterQuery);
        }

        return new RagSearchResult(hits, result.Plan, result.SearchFailed);
    }

    public static IReadOnlyList<RagSearchHit> FilterWeakHits(
        IReadOnlyList<RagSearchHit> hits,
        string query)
    {
        var needles = ExtractNeedles(query);
        if (needles.Count == 0 || hits.Count == 0)
            return Array.Empty<RagSearchHit>();

        var kept = new List<RagSearchHit>(hits.Count);
        foreach (var hit in hits)
        {
            var hay = BuildHaystack(hit);
            if (needles.Any(n => hay.Contains(n, StringComparison.OrdinalIgnoreCase)))
                kept.Add(hit);
        }

        return kept;
    }

    public static IReadOnlyList<RagSearchHit> PreferLegalSources(IReadOnlyList<RagSearchHit> hits)
    {
        if (hits.Count == 0)
            return hits;

        var legal = hits.Where(IsLegalSource).ToList();
        return legal.Count > 0 ? legal : hits;
    }

    public static bool IsLegalSource(RagSearchHit hit)
    {
        var name = hit.SourceFileName ?? "";
        foreach (var hint in LegalSourceHints)
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildHaystack(RagSearchHit hit) =>
        string.Concat(
            hit.SourceFileName, "\n",
            hit.HeaderText, "\n",
            hit.SectionPath, "\n",
            hit.PromptText, "\n",
            hit.DefinitionLead, "\n",
            hit.PenaltyLead);

    internal static List<string> ExtractNeedles(string query)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(query))
            return list;

        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length < 2)
            {
                sb.Clear();
                return;
            }

            var token = sb.ToString();
            sb.Clear();
            if (NeedleStopwords.Contains(token))
                return;
            if (!list.Contains(token, StringComparer.OrdinalIgnoreCase))
                list.Add(token);

            // 日本語向けに 2 文字スライディングも追加（長めの語から）
            if (token.Length >= 4 && token.Any(IsCjk))
            {
                for (var i = 0; i <= token.Length - 2; i++)
                {
                    var bi = token.Substring(i, 2);
                    if (NeedleStopwords.Contains(bi))
                        continue;
                    if (!list.Contains(bi, StringComparer.Ordinal))
                        list.Add(bi);
                }
            }
        }

        foreach (var ch in query.Trim())
        {
            if (char.IsLetterOrDigit(ch) || IsCjk(ch))
            {
                sb.Append(ch);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return list;
    }

    private static bool IsCjk(char ch) =>
        ch is (>= '\u3040' and <= '\u30FF')
            or (>= '\u3400' and <= '\u9FFF')
            or (>= '\uF900' and <= '\uFAFF');
}
