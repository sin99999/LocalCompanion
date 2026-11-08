using System.Diagnostics;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 返す直前だけ、拾った断片を Julia がふるう。Gemma が返事を書く前。
/// 止まっているとき、読めないときは、拾った一覧のまま。
/// </summary>
internal static class RagJuliaHitFilter
{
    public static async Task<RagSearchResult> ApplyAsync(
        RagSearchResult result,
        CancellationToken ct)
    {
        if (result.Hits.Count == 0 || !LayaSidecar.IsReady)
            return result;

        var question = result.Plan.EffectiveQuery;
        if (string.IsNullOrWhiteSpace(question))
            return result;

        var yes = new List<double?>(Math.Min(result.Hits.Count, RagJuliaAnswerCheck.MaxJudgedHits));
        var clock = Stopwatch.StartNew();
        var limit = Math.Min(result.Hits.Count, RagJuliaAnswerCheck.MaxJudgedHits);
        for (var i = 0; i < limit; i++)
        {
            if (clock.Elapsed > RagJuliaAnswerCheck.TimeBudget)
                break;
            ct.ThrowIfCancellationRequested();
            var hit = result.Hits[i];
            var passage = string.IsNullOrWhiteSpace(hit.HeaderText)
                ? hit.Text
                : hit.HeaderText + "\n" + hit.Text;
            yes.Add(await LayaSidecar.TryPassageAnswersYesAsync(passage, question, ct));
        }

        if (yes.Count == 0)
            return result;

        var kept = RagJuliaAnswerCheck.Apply(result.Hits, yes);
        if (kept.Count == result.Hits.Count)
            return result;

        StartupLog.Write($"Julia rag hits {kept.Count}/{result.Hits.Count}");
        return new RagSearchResult(kept, result.Plan, result.SearchFailed);
    }
}
