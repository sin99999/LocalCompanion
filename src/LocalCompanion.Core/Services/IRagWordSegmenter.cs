using LocalCompanion.Data;

namespace LocalCompanion.Services;

/// <summary>
/// 概念抽出用の分かち書き。Core は依存なしのヒューリスティック、シェルは Windows の WordsSegmenter を差し込む。
/// 返す語は「内容語らしいもの」だけでよく、助詞・活用は落としてよい。正規化は呼び出し側（<see cref="RagConceptExtractor"/>）。
/// </summary>
public interface IRagWordSegmenter
{
    IReadOnlyList<string> Segment(string text);
}

/// <summary>
/// 依存なしの分かち書き。漢字列・カタカナ列・英数字列（2 文字以上）をそのまま語にする（<see cref="RagFtsSegmenter"/> と同じ切り方）。
/// 漢字列は法令の接続語（又・及・若・並）で切る（「懲役又は」→「懲役」）。テストと、WordsSegmenter が使えない環境の既定。
/// </summary>
public sealed class HeuristicWordSegmenter : IRagWordSegmenter
{
    private static readonly char[] Conjunctions = ['又', '及', '若', '並'];

    public IReadOnlyList<string> Segment(string text)
    {
        var words = new List<string>();
        foreach (var run in RagFtsSegmenter.EnumerateContentRuns(text))
        {
            if (run.IndexOfAny(Conjunctions) < 0)
            {
                words.Add(run);
                continue;
            }

            foreach (var part in run.Split(Conjunctions, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 2)
                    words.Add(part);
            }
        }

        return words;
    }
}
