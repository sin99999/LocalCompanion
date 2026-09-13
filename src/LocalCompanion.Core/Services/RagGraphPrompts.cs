using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>俯瞰検索で使う短い LLM プロンプトと、その返答の読み取り。小さいモデル前提で 1 つずつ簡単に頼む。</summary>
internal static class RagGraphPrompts
{
    public const string RefineSystem =
        "あなたは検索語を作る係です。質問に答えるために資料を探すときの検索語を、1 行に 1 つ、2〜3 行だけ書いてください。説明や番号は書かないでください。";

    public static string RefineUser(string query) => $"質問: {query}";

    public const string RelevanceSystem =
        "資料が質問に答える材料になるかを判定します。なるなら 1、ならないなら 0 だけを書いてください。";

    public static string RelevanceUser(string query, RagSearchHit hit)
    {
        var body = hit.Text.Length > 600 ? hit.Text[..600] : hit.Text;
        return $"質問: {query}\n資料（{hit.HeaderText}）:\n{body}";
    }

    public const string MapSystem =
        "資料から質問に関係する要点だけを、3 行以内の箇条書きで書いてください。資料に書いてないことは書かず、根拠が無ければ「該当なし」とだけ書いてください。";

    public static string MapUser(string query, IReadOnlyList<RagSearchHit> chunks)
    {
        var parts = chunks.Select((c, i) =>
        {
            var body = c.Text.Length > 700 ? c.Text[..700] : c.Text;
            return $"[{i + 1}] {c.HeaderText}\n{body}";
        });
        return $"質問: {query}\n\n資料:\n{string.Join("\n\n", parts)}";
    }

    public static IReadOnlyList<string> ParseLines(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return Array.Empty<string>();

        return reply
            .Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '・', '•', ' ').Trim())
            .Select(l => l.Length > 2 && char.IsDigit(l[0]) && (l[1] is '.' or ')' or '、') ? l[2..].Trim() : l)
            .Where(l => l.Length >= 2 && l.Length <= 60)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool ParseYes(string reply)
    {
        var t = reply.Trim();
        if (t.Length == 0)
            return false;
        return t[0] == '1' || t.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || t.StartsWith("はい", StringComparison.Ordinal);
    }

    public static bool IsNoContent(string reply) =>
        reply.Trim().TrimStart('-', '・', ' ').StartsWith("該当なし", StringComparison.Ordinal);
}
