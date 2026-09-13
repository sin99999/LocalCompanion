using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>このターンのプロンプトを組むのに要る材料（取得は呼び出し側、判断はここ）。</summary>
internal sealed record ChatPromptContext(
    bool IsCharacter,
    CharacterProfileDto Profile,
    string? UserDisplayName,
    string? MemoryBlock,
    ModelRuntimeStatus Runtime,
    bool HasAttachment,
    bool HasImage,
    bool Japanese,
    string UserMessage,
    int MaxReplyChars);

/// <summary>
/// システムプロンプトの組み立て。今のターンに関係する部品だけ載せ、返答言語の指示は最後に 1 回だけ置く。
/// 並び: 役割・名前 → 長期記憶 → 書き方 → 実行環境 → 添付 → （呼び出し側が足す保存・参考資料）→ 出どころ → 返答言語。
/// </summary>
internal static class ChatPromptAssembler
{
    /// <summary>参考資料・保存指示より前に置く部品。</summary>
    public static List<string> BuildCoreParts(ChatPromptContext ctx)
    {
        var ja = ctx.Japanese;
        var parts = new List<string>();

        var userName = ctx.UserDisplayName?.Trim();
        var characterName = ctx.IsCharacter ? ctx.Profile.Name?.Trim() : null;
        if (ctx.IsCharacter)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Profile.Persona))
                parts.Add(ctx.Profile.Persona.Trim());
            if (!string.IsNullOrWhiteSpace(ctx.Profile.SpeakingStyle))
                parts.Add(ChatSystemPromptTexts.SpeakingStyleLine(ctx.Profile.SpeakingStyle.Trim(), ja));
        }
        else
        {
            parts.Add(ChatSystemPromptTexts.DefaultAssistantLine(ja));
        }

        // 名前は 1 ブロックだけ。両方あるときは区別表、片方だけならその行
        if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(characterName))
            parts.Add(ChatSystemPromptTexts.UserAndCharacterNameDistinction(userName, characterName, ja));
        else if (!string.IsNullOrEmpty(userName))
            parts.Add(ChatSystemPromptTexts.UserNameLine(userName, ja));
        else if (!string.IsNullOrEmpty(characterName))
            parts.Add(ChatSystemPromptTexts.CharacterNameLine(characterName, ja));

        if (!string.IsNullOrWhiteSpace(ctx.MemoryBlock))
        {
            parts.Add(ctx.MemoryBlock);
            parts.Add(ChatSystemPromptTexts.SpontaneousMemoryInstruction(ja));
        }

        parts.Add(ChatSystemPromptTexts.ReadabilityInstruction(ja));
        parts.Add(ChatReplyLimitHelper.SystemLimitNote(ctx.MaxReplyChars, ja));
        if (ChatReplyLimitHelper.UserRequestsExcessiveLength(ctx.UserMessage, ctx.MaxReplyChars))
            parts.Add(ChatReplyLimitHelper.ExcessiveRequestNote(ctx.MaxReplyChars, ja));

        var runtime = ctx.Runtime;
        if (!string.IsNullOrWhiteSpace(runtime.LoadedModelFileName))
            parts.Add(ChatSystemPromptTexts.LoadedModelLine(runtime.LoadedModelFileName, ja));
        else if (!string.IsNullOrWhiteSpace(runtime.SelectedModelFileName))
            parts.Add(ChatSystemPromptTexts.SelectedModelLine(runtime.SelectedModelFileName, ja));
        if (runtime.ModelMismatch)
            parts.Add(ChatSystemPromptTexts.ModelMismatchLine(runtime.SelectedModelFileName ?? "", runtime.LoadedModelFileName ?? "", ja));
        if (!string.IsNullOrWhiteSpace(runtime.MmprojWarning))
            parts.Add(runtime.MmprojWarning);

        if (ctx.HasAttachment)
            parts.Add(ChatSystemPromptTexts.AttachmentInstruction(ja));
        if (ctx.HasImage)
            parts.Add(ChatSystemPromptTexts.ImageInstruction(ja));

        return parts;
    }

    /// <summary>参考資料などを足し終えた部品列を 1 本の system テキストにする。出どころの区別と返答言語を末尾に置く。</summary>
    public static string Compose(IReadOnlyList<string> parts, ChatPromptContext ctx, bool hasRagBlock)
    {
        var all = new List<string>(parts.Count + 2);
        all.AddRange(parts);
        if (hasRagBlock || ctx.HasAttachment)
            all.Add(ChatSystemPromptTexts.MemoryDistinction(ctx.Japanese));
        all.Add(ChatSystemPromptTexts.ReplyLanguageInstruction(ctx.Japanese, ctx.IsCharacter));
        return string.Join("\n\n", all);
    }
}
