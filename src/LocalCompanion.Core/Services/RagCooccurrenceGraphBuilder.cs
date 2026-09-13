using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 共起辺。チャンク全体ではなく「文 3 つの窓」で数える（900 字チャンクだと全体共起は密すぎる）。
/// 数えるのはチャンクの上位 K 語どうしだけ。合計 weight が下限未満の辺は捨てる（refers は別系統で捨てない）。
/// 見出し語との対は <see cref="HeaderPairWeight"/>（条文 1 文チャンクでも見出し↔本文の骨は残る）。
/// </summary>
internal sealed class RagCooccurrenceGraphBuilder
{
    public const int DefaultWindowSentences = 3;
    public const double DefaultMinEdgeWeight = 2.0;
    public const double HeaderPairWeight = 2.0;

    private static readonly Regex SentenceSplit = new(@"(?<=[。！？!?\n])", RegexOptions.Compiled);

    private readonly IRagWordSegmenter _segmenter;
    private readonly int _windowSentences;

    public RagCooccurrenceGraphBuilder(IRagWordSegmenter segmenter, int windowSentences = DefaultWindowSentences)
    {
        _segmenter = segmenter;
        _windowSentences = Math.Max(1, windowSentences);
    }

    /// <summary>source 内の全チャンクから共起辺を作る（枝刈り済み、NormA &lt; NormB）。</summary>
    public IReadOnlyList<RagConceptEdgeDraft> Build(
        IReadOnlyList<(RagGraphChunkInput Chunk, IReadOnlyList<string> TopTerms)> chunks,
        double minEdgeWeight = DefaultMinEdgeWeight)
    {
        var weights = new Dictionary<(string, string), double>();
        foreach (var (chunk, topTerms) in chunks)
        {
            if (topTerms.Count < 2)
                continue;

            var allowed = new HashSet<string>(topTerms, StringComparer.Ordinal);
            var sentences = SentenceTermSets(chunk, allowed);

            // 見出しの語は全窓に効かせる（見出し×本文の結び付きが章の骨になる）
            var headerTerms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in _segmenter.Segment(chunk.HeaderText))
            {
                var norm = RagConceptExtractor.Normalize(word);
                if (allowed.Contains(norm))
                    headerTerms.Add(norm);
            }

            for (var start = 0; start < sentences.Count; start += 1)
            {
                var window = new HashSet<string>(headerTerms, StringComparer.Ordinal);
                for (var i = start; i < Math.Min(sentences.Count, start + _windowSentences); i++)
                    window.UnionWith(sentences[i]);

                CountPairs(window, headerTerms, weights);
                if (start + _windowSentences >= sentences.Count)
                    break;
            }

            if (sentences.Count == 0 && headerTerms.Count >= 2)
                CountPairs(headerTerms, headerTerms, weights);
        }

        return weights
            .Where(kv => kv.Value >= minEdgeWeight)
            .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .Select(kv => new RagConceptEdgeDraft(kv.Key.Item1, kv.Key.Item2, RagConceptEdgeDraft.KindCooccurrence, kv.Value))
            .ToList();
    }

    private List<HashSet<string>> SentenceTermSets(RagGraphChunkInput chunk, HashSet<string> allowed)
    {
        var list = new List<HashSet<string>>();
        foreach (var sentence in SentenceSplit.Split(chunk.Text ?? ""))
        {
            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in _segmenter.Segment(sentence))
            {
                var norm = RagConceptExtractor.Normalize(word);
                if (allowed.Contains(norm))
                    set.Add(norm);
            }

            if (set.Count > 0)
                list.Add(set);
        }

        return list;
    }

    private static void CountPairs(HashSet<string> terms, HashSet<string> headerTerms, Dictionary<(string, string), double> weights)
    {
        if (terms.Count < 2)
            return;

        var sorted = terms.OrderBy(t => t, StringComparer.Ordinal).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            for (var j = i + 1; j < sorted.Count; j++)
            {
                var key = (sorted[i], sorted[j]);
                var weight = headerTerms.Contains(sorted[i]) || headerTerms.Contains(sorted[j]) ? HeaderPairWeight : 1.0;
                weights[key] = weights.GetValueOrDefault(key) + weight;
            }
        }
    }
}
