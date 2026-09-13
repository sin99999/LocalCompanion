using System.Text;
using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// チャンクから概念（語）を拾う。LLM も埋め込みも使わない。
/// 見出し語・entry_key・条番号は重く、本文の語は回数どおり。ペア化に使うのは上位 K 語だけ（関係爆発を防ぐ）。
/// </summary>
internal sealed class RagConceptExtractor
{
    /// <summary>ペア化・共起に使う 1 チャンクあたりの上位語数。</summary>
    public const int DefaultTopK = 15;

    /// <summary>これより多くのチャンクに現れる語は概念にしない（IDF 的上限）。</summary>
    public const double DefaultMaxDocRatio = 0.4;

    /// <summary>小さなコーパスでは上限を掛けない（数チャンクなら 40% はすぐ超える）。</summary>
    public const int MinChunksForDocRatio = 10;

    private static readonly Regex ArticleToken = new(@"第\s*(\d+)\s*条", RegexOptions.Compiled);

    private readonly IRagWordSegmenter _segmenter;

    public RagConceptExtractor(IRagWordSegmenter segmenter)
    {
        _segmenter = segmenter;
    }

    /// <summary>1 チャンクの概念（回数付き、回数降順）。</summary>
    public IReadOnlyList<RagConceptOccurrence> Extract(RagGraphChunkInput chunk)
    {
        var counts = new Dictionary<string, (string Term, RagConceptKind Kind, int Count)>(StringComparer.Ordinal);

        void Add(string raw, RagConceptKind kind, int weight)
        {
            var norm = Normalize(raw);
            if (norm.Length < 2 || RagConceptStopwords.IsStopword(norm) || IsAllDigits(norm))
                return;

            if (counts.TryGetValue(norm, out var cur))
                counts[norm] = (cur.Term, Stronger(cur.Kind, kind), cur.Count + weight);
            else
                counts[norm] = (raw.Trim(), kind, weight);
        }

        if (chunk.ArticleMain > 0)
            Add($"第{chunk.ArticleMain}条", RagConceptKind.Article, 3);

        if (!string.IsNullOrWhiteSpace(chunk.EntryKey))
            Add(chunk.EntryKey, RagConceptKind.Entry, 3);

        if (!string.IsNullOrWhiteSpace(chunk.HeaderText))
        {
            foreach (var word in _segmenter.Segment(StripArticleNumbers(chunk.HeaderText)))
                Add(word, RagConceptKind.Heading, 2);
        }

        if (!string.IsNullOrWhiteSpace(chunk.Text))
        {
            foreach (var word in _segmenter.Segment(StripArticleNumbers(chunk.Text)))
                Add(word, RagConceptKind.Term, 1);
        }

        return counts
            .Select(kv => new RagConceptOccurrence(kv.Value.Term, kv.Key, kv.Value.Kind, kv.Value.Count))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.NormTerm, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>コーパス全体で「どこにでも出る語」を落とす。</summary>
    public static IReadOnlyList<RagChunkConcepts> ApplyDocRatioCap(
        IReadOnlyList<RagChunkConcepts> chunks,
        double maxDocRatio = DefaultMaxDocRatio)
    {
        if (chunks.Count < MinChunksForDocRatio)
            return chunks;

        var docFreq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            foreach (var c in chunk.Concepts)
                docFreq[c.NormTerm] = docFreq.GetValueOrDefault(c.NormTerm) + 1;
        }

        var limit = Math.Max(1, (int)Math.Floor(chunks.Count * maxDocRatio));
        var result = new List<RagChunkConcepts>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var kept = chunk.Concepts
                .Where(c => c.Kind == RagConceptKind.Article || docFreq[c.NormTerm] <= limit)
                .ToList();
            result.Add(new RagChunkConcepts(chunk.ChunkId, kept));
        }

        return result;
    }

    /// <summary>ペア化に使う上位 K 語の正規化形。</summary>
    public static IReadOnlyList<string> TopTerms(IReadOnlyList<RagConceptOccurrence> concepts, int topK = DefaultTopK) =>
        concepts.Take(topK).Select(c => c.NormTerm).ToList();

    /// <summary>NFKC・小文字・中黒と空白の除去。索引と質問で同じ形にする。</summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var text = raw.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '・' or '･' || char.IsWhiteSpace(ch))
                continue;
            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>条番号は Article 概念として別に足すので、本文からは外して分かち書きの語にしない。</summary>
    private static string StripArticleNumbers(string text) => ArticleToken.Replace(text, " ");

    private static bool IsAllDigits(string s) => s.All(char.IsDigit);

    private static RagConceptKind Stronger(RagConceptKind a, RagConceptKind b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(RagConceptKind kind) => kind switch
    {
        RagConceptKind.Article => 3,
        RagConceptKind.Entry => 2,
        RagConceptKind.Heading => 1,
        _ => 0,
    };
}
