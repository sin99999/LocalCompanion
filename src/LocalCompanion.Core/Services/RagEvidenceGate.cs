using LocalCompanion.Data;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 根拠門番。合図語（法律・資料…）が無い一般文でも、資料に質問の語が実在するときだけ載せる。
/// 検索前は「内容語があるか」で足切りし、検索後は「ヒット本文に質問の語がそのまま在るか」で残す。
/// 語彙判定だけで、埋め込みは使わない（毎ターン走るので安く保つ）。
/// </summary>
internal static class RagEvidenceGate
{
    /// <summary>雑談に混ぜる資料は少なく（プロンプト予算と話の温度のため）。</summary>
    public const int MaxProbeHits = 3;

    /// <summary>検索する価値があるか。ひらがなだけの挨拶・相槌・1 文字語だけの文は false。</summary>
    public static bool IsWorthProbing(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return RagFtsSegmenter.ContentRuns(message).Count > 0;
    }

    /// <summary>質問の内容語がそのまま含まれるヒットだけ残す（順位は検索のまま、上位 <see cref="MaxProbeHits"/> 件）。</summary>
    public static IReadOnlyList<RagSearchHit> KeepEvidenced(IReadOnlyList<RagSearchHit> hits, string? message)
    {
        var runs = RagFtsSegmenter.ContentRuns(message);
        if (runs.Count == 0 || hits.Count == 0)
            return Array.Empty<RagSearchHit>();

        var kept = new List<RagSearchHit>(MaxProbeHits);
        foreach (var hit in hits)
        {
            if (!HasEvidence(hit, runs))
                continue;

            kept.Add(hit);
            if (kept.Count >= MaxProbeHits)
                break;
        }

        return kept;
    }

    private static bool HasEvidence(RagSearchHit hit, IReadOnlyList<string> runs)
    {
        var hay = string.Concat(
            hit.SourceFileName, "\n",
            hit.HeaderText, "\n",
            hit.SectionPath, "\n",
            hit.PromptText);
        foreach (var run in runs)
        {
            if (hay.Contains(run, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
