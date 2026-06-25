using LocalCompanion.Services;

namespace LocalCompanion.Localization;

public static class UserFacingErrorLocalizer
{
    public static string Localize(Exception ex, LocalizationService? loc = null)
    {
        loc ??= LocalizationService.Instance;

        if (ex is LocalizedServiceException lse)
        {
            return lse.FormatArgs.Length > 0
                ? loc.Format(lse.LocalizationKey, lse.FormatArgs)
                : loc.Get(lse.LocalizationKey);
        }

        var msg = ex.Message;
        if (msg == LlamaServerClient.ConnectionFailedMessage
            || msg == LlamaServerClient.ContextOverflowMessage
            || msg == loc.Get("Error.LlamaConnectionFailed")
            || msg == loc.Get("Error.ContextOverflow")
            || msg == loc.Get("Startup.LlamaFailed"))
            return msg;

        if (TryLocalizeLegacyMessage(ex, loc, out var legacy))
            return legacy;

        return loc.Get("Error.Unexpected");
    }

    private static bool TryLocalizeLegacyMessage(Exception ex, LocalizationService loc, out string message)
    {
        message = "";
        var msg = ex.Message;

        if (ex is NotSupportedException && TryExtractSuffix(msg, "未対応の形式:", out var ext)
            || ex is NotSupportedException && TryExtractSuffix(msg, "Unsupported format:", out ext))
        {
            message = loc.Format("Settings.Rag.Error.UnsupportedFormat", ext.Trim());
            return true;
        }

        if (msg == "ファイルが見つかりません" || msg == "File not found.")
        {
            message = loc.Get("Settings.Rag.Error.FileNotFound");
            return true;
        }

        if (msg == "パスが見つかりません" || msg == "Path not found.")
        {
            message = loc.Get("Settings.Rag.Error.PathNotFound");
            return true;
        }

        if (TryExtractPrefix(msg, "ファイルが大きすぎます（上限 ", out var tail)
            && tail.EndsWith("MB）", StringComparison.Ordinal))
        {
            var mb = tail[..^3].Trim();
            message = loc.Format("Settings.Rag.Error.FileTooLarge", mb);
            return true;
        }

        if (TryExtractPrefix(msg, "File is too large (limit ", out tail)
            && tail.EndsWith(" MB)", StringComparison.Ordinal))
        {
            var mb = tail[..^3].Trim();
            message = loc.Format("Settings.Rag.Error.FileTooLarge", mb);
            return true;
        }

        if (msg == "Word 文書の本文が見つかりません"
            || msg == "Word document body was not found.")
        {
            message = loc.Get("Settings.Rag.Error.WordBodyMissing");
            return true;
        }

        if (msg.StartsWith(
                "RAG用の埋め込みAPIが使えません",
                StringComparison.Ordinal)
            || msg.StartsWith(
                "The embedding API for RAG is unavailable",
                StringComparison.Ordinal))
        {
            message = loc.Get("Settings.Rag.Error.EmbeddingsUnavailable");
            return true;
        }

        if (TryExtractPrefix(msg, "キャラ設定が見つかりません:", out var name)
            || TryExtractPrefix(msg, "Character preset not found:", out name))
        {
            message = loc.Format("Settings.Character.Error.NotFound", name.Trim());
            return true;
        }

        if (TryExtractPrefix(msg, "読み込みに失敗しました:", out name)
            || TryExtractPrefix(msg, "Failed to load:", out name))
        {
            message = loc.Format("Settings.Character.Error.LoadFailed", name.Trim());
            return true;
        }

        if (msg == "無効なファイル名です。" || msg == "Invalid file name.")
        {
            message = loc.Get("Settings.Character.Error.InvalidFileName");
            return true;
        }

        if (msg == "selection.json は削除できません。"
            || msg == "selection.json cannot be deleted.")
        {
            message = loc.Get("Settings.Character.Error.CannotDeleteSelection");
            return true;
        }

        if (TryExtractPrefix(msg, "モデルが見つかりません:", out name)
            || TryExtractPrefix(msg, "Model not found:", out name))
        {
            message = loc.Format("Settings.Model.Error.NotFound", name.Trim());
            return true;
        }

        if (TryExtractPrefix(msg, "mmproj が見つかりません:", out name)
            || TryExtractPrefix(msg, "mmproj not found:", out name))
        {
            message = loc.Format("Settings.Model.Error.MmprojNotFound", name.Trim());
            return true;
        }

        if (msg.StartsWith("選んだ mmproj はこの本体と合いません", StringComparison.Ordinal)
            || msg.StartsWith("The selected mmproj is incompatible", StringComparison.Ordinal))
        {
            message = loc.Get("Settings.Model.Error.MmprojIncompatible");
            return true;
        }

        if (msg == "キャラクターが選択されていないため、会話セッションを作成できません。"
            || msg == "Cannot create a conversation session because no character is selected.")
        {
            message = loc.Get("Chat.Error.NoCharacterSelected");
            return true;
        }

        if (msg.StartsWith("応答が空でした", StringComparison.Ordinal)
            || msg.StartsWith("The response was empty.", StringComparison.Ordinal))
        {
            message = loc.Get("Chat.Error.EmptyModelReply");
            return true;
        }

        if (msg.StartsWith("画像入力に失敗しました", StringComparison.Ordinal)
            || msg.StartsWith("Image input failed.", StringComparison.Ordinal))
        {
            message = loc.Get("Chat.Error.VisionFailed");
            return true;
        }

        if (msg == "26B 用 mmproj が必要です"
            || msg == "An mmproj for 26B is required.")
        {
            message = loc.Get("Settings.Model.Error.MmprojRequired26B");
            return true;
        }

        if (msg == "E2B 用 mmproj が必要です"
            || msg == "An mmproj for E2B is required.")
        {
            message = loc.Get("Settings.Model.Error.MmprojRequiredE2B");
            return true;
        }

        if (msg.StartsWith("llama-server のモデル ID", StringComparison.Ordinal)
            || msg.StartsWith("Could not obtain a model ID from llama-server", StringComparison.Ordinal))
        {
            message = loc.Get("Error.LlamaModelIdUnavailable");
            return true;
        }

        if (msg == "ファイル選択ダイアログがタイムアウトしました。"
            || msg == "The file picker timed out.")
        {
            message = loc.Get("Settings.Rag.Error.PickerTimeout");
            return true;
        }

        if (msg == "download invalid" || msg == "ダウンロードしたファイルが不正です。")
        {
            message = loc.Get("Startup.Error.DownloadInvalid");
            return true;
        }

        return false;
    }

    private static bool TryExtractSuffix(string message, string prefix, out string suffix)
    {
        if (message.StartsWith(prefix, StringComparison.Ordinal))
        {
            suffix = message[prefix.Length..];
            return true;
        }

        suffix = "";
        return false;
    }

    private static bool TryExtractPrefix(string message, string prefix, out string suffix)
    {
        if (message.StartsWith(prefix, StringComparison.Ordinal))
        {
            suffix = message[prefix.Length..];
            return true;
        }

        suffix = "";
        return false;
    }
}
