namespace LocalCompanion.Services;

/// <summary>
/// SoftTopic / 一般検索向けの軽い言い換え展開（HyDE ではない）。
/// 「残業」→本文に出やすい「労働時間」などを足して FTS・弱ヒット床の取りこぼしを減らす。
/// </summary>
internal static class RagSoftQueryExpander
{
    private static readonly (string Cue, string Extra)[] Rules =
    [
        ("残業", "労働時間 四十時間 休憩 時間外"),
        ("オーバータイム", "労働時間 四十時間 時間外"),
        ("overtime", "労働時間 四十時間 working hours"),
        ("万引き", "窃盗 財物 窃取"),
        ("盗み", "窃盗 財物 窃取"),
        ("盗む", "窃盗 財物 窃取"),
        ("殺す", "殺人 殺害"),
        ("殺人", "殺人 殺害"),
        ("殺害", "殺人 殺害"),
        ("murder", "殺人 殺害 homicide"),
        ("略取", "略取 誘拐 監禁"),
        ("誘拐", "略取 誘拐 監禁"),
        ("不同意性交", "不同意性交 強制性交 わいせつ"),
        ("強制性交", "不同意性交 強制性交 わいせつ"),
        ("わいせつ", "わいせつ 不同意性交"),
    ];

    public static string Expand(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query ?? "";

        var extras = new List<string>();
        foreach (var (cue, extra) in Rules)
        {
            if (query.Contains(cue, StringComparison.OrdinalIgnoreCase))
                extras.Add(extra);
        }

        if (extras.Count == 0)
            return query.Trim();

        return (query.Trim() + " " + string.Join(" ", extras)).Trim();
    }
}
