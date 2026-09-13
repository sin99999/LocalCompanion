using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests.Fixtures;

/// <summary>
/// 俯瞰検索用の LLM 代役。system 文で役割（検索語分け／関連判定／要点圧縮）を見分けて台本どおり返す。
/// <see cref="Dead"/> は全部 null（llama 停止の再現）。
/// </summary>
internal sealed class FakeGraphLlm : IRagGraphLlm
{
    private readonly Func<string, string, string?> _script;

    public FakeGraphLlm(Func<string, string, string?> script) => _script = script;

    public int Calls { get; private set; }
    public int RelevanceCalls { get; private set; }
    public int MapCalls { get; private set; }
    public int RefineCalls { get; private set; }

    public Task<string?> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        Calls++;
        if (ReferenceEquals(system, RagGraphPrompts.RelevanceSystem))
            RelevanceCalls++;
        else if (ReferenceEquals(system, RagGraphPrompts.MapSystem))
            MapCalls++;
        else if (ReferenceEquals(system, RagGraphPrompts.RefineSystem))
            RefineCalls++;
        return Task.FromResult(_script(system, user));
    }

    public static FakeGraphLlm Dead { get; } = new((_, _) => null);

    /// <summary>関連判定は「質問の内容語が資料に出ていれば 1」。分割は質問の内容語、要点は資料見出しを並べた 2 行。</summary>
    public static FakeGraphLlm JudgeByWords(string query, string? mapReply = null)
    {
        var words = new HeuristicWordSegmenter().Segment(query)
            .Where(w => !RagConceptStopwords.IsStopword(RagConceptExtractor.Normalize(w)))
            .Distinct()
            .ToList();
        return new FakeGraphLlm((system, user) =>
        {
            if (ReferenceEquals(system, RagGraphPrompts.RefineSystem))
                return string.Join("\n", words.Take(3));
            if (ReferenceEquals(system, RagGraphPrompts.RelevanceSystem))
            {
                var body = user[(user.IndexOf('\n') + 1)..];
                return words.Any(w => body.Contains(w, StringComparison.Ordinal)) ? "1" : "0";
            }

            return mapReply ?? "- 資料に基づく要点 1\n- 資料に基づく要点 2";
        });
    }
}
