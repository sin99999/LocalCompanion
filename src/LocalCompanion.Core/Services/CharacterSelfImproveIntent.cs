namespace LocalCompanion.Services;

/// <summary>ユーザーがキャラ JSON／persona の更新提案を求めているか（会話からの機械検出）。</summary>
public static class CharacterSelfImproveIntent
{
    /// <summary>
    /// 「.json に書いて」「性格を提案して」「ルールを提案して」など、明示的に設定更新を求めているか。
    /// </summary>
    public static bool LooksLikePersonaUpdateRequest(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var t = userMessage.Trim();
        if (t.Length > 500)
            t = t[..500];

        var mentionsTarget =
            t.Contains(".json", StringComparison.OrdinalIgnoreCase)
            || t.Contains("キャラクター設定", StringComparison.Ordinal)
            || t.Contains("キャラ設定", StringComparison.Ordinal)
            || t.Contains("性格・指示", StringComparison.Ordinal)
            || t.Contains("システムプロンプト", StringComparison.Ordinal)
            || t.Contains("persona", StringComparison.OrdinalIgnoreCase)
            || t.Contains("character settings", StringComparison.OrdinalIgnoreCase)
            || t.Contains("personality", StringComparison.OrdinalIgnoreCase)
            || t.Contains("容姿", StringComparison.Ordinal)
            || t.Contains("外見", StringComparison.Ordinal)
            || t.Contains("見た目", StringComparison.Ordinal)
            || t.Contains("プロフィール", StringComparison.Ordinal)
            || t.Contains("appearance", StringComparison.OrdinalIgnoreCase);

        var asksUpdate =
            t.Contains("提案", StringComparison.Ordinal)
            || t.Contains("記述", StringComparison.Ordinal)
            || t.Contains("記載", StringComparison.Ordinal)
            || t.Contains("書いて", StringComparison.Ordinal)
            || t.Contains("書き込", StringComparison.Ordinal)
            || t.Contains("反映", StringComparison.Ordinal)
            || t.Contains("更新して", StringComparison.Ordinal)
            || t.Contains("保存して", StringComparison.Ordinal)
            || t.Contains("追記", StringComparison.Ordinal)
            || t.Contains("育てて", StringComparison.Ordinal)
            || t.Contains("入れて", StringComparison.Ordinal)
            || t.Contains("挙げて", StringComparison.Ordinal)
            || t.Contains("まとめて", StringComparison.Ordinal)
            || t.Contains("作って", StringComparison.Ordinal)
            || t.Contains("propose", StringComparison.OrdinalIgnoreCase)
            || t.Contains("update the persona", StringComparison.OrdinalIgnoreCase)
            || t.Contains("write into", StringComparison.OrdinalIgnoreCase);

        if (mentionsTarget && asksUpdate)
            return true;

        // 「性格を提案して」「数値を記述して」「ルールを提案して」系（.json 無し）
        if (asksUpdate
            && (t.Contains("性格", StringComparison.Ordinal)
                || t.Contains("この性格", StringComparison.Ordinal)
                || t.Contains("その性格", StringComparison.Ordinal)
                || t.Contains("きっちり", StringComparison.Ordinal)
                || t.Contains("口調", StringComparison.Ordinal)
                || t.Contains("キャラ", StringComparison.Ordinal)
                || t.Contains("容姿", StringComparison.Ordinal)
                || t.Contains("外見", StringComparison.Ordinal)
                || t.Contains("見た目", StringComparison.Ordinal)
                || t.Contains("数値", StringComparison.Ordinal)
                || t.Contains("三サイズ", StringComparison.Ordinal)
                || t.Contains("呼び方", StringComparison.Ordinal)
                || t.Contains("ルール", StringComparison.Ordinal)
                || t.Contains("か条", StringComparison.Ordinal)
                || t.Contains("条項", StringComparison.Ordinal)
                || t.Contains("指針", StringComparison.Ordinal)))
            return true;

        return false;
    }
}
