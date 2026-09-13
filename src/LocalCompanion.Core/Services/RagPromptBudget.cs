using System.Text;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 参考資料ブロックの文字数予算。親セクション全文（parent_text）が載ると 1 件で数千字になり、
/// 小さなコンテキストを食い尽くすので、1 件と全体の上限で切る。
/// </summary>
internal static class RagPromptBudget
{
    /// <summary>1 件あたりの本文上限。既定チャンク（900 字）は切れない。</summary>
    public const int DefaultHitChars = 900;

    /// <summary>ブロック全体の上限（見出し・引用行込み）。</summary>
    public const int DefaultBlockChars = 3600;

    public static string FormatHits(
        IReadOnlyList<RagSearchHit> hits,
        RagQueryPlan plan,
        int hitChars = DefaultHitChars,
        int blockChars = DefaultBlockChars)
    {
        if (hits.Count == 0)
            return "";

        var sb = new StringBuilder();
        var index = 0;
        string? currentGroup = null;
        foreach (var hit in hits)
        {
            var piece = new StringBuilder();
            if (plan.Intent == RagQueryIntent.Advisory && !string.Equals(currentGroup, hit.SourceFileName, StringComparison.Ordinal))
            {
                currentGroup = hit.SourceFileName;
                index = 0;
                piece.Append("■ ").Append(hit.SourceFileName).Append("\n\n");
            }

            piece.Append(hit.FormatForPrompt(index, hitChars));
            // 1 件目は必ず載せる。2 件目以降は全体予算を超えるなら打ち切る
            if (sb.Length > 0 && sb.Length + piece.Length + 2 > blockChars)
                break;

            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(piece);
            index++;
        }

        return sb.ToString();
    }
}
