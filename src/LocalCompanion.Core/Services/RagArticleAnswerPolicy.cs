using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 条番号の法令質問を LLM に渡すと、別条を「関連」として混ぜることがある。
/// 機械引用するか、キャラクター向けの Rag プロンプトを使うかの判定だけを持つ。
/// </summary>
internal static class RagArticleAnswerPolicy
{
    public static bool AllowCharacterVerbatim(bool isCharacter, RagQueryPlan plan, string userMessage)
    {
        if (!isCharacter)
            return true;

        if (plan.Intent is RagQueryIntent.Article or RagQueryIntent.SourceCatalog)
            return true;

        return RagFormalLegalCue.IsFormalLegalQuery(userMessage);
    }

    public static bool UsePersonaRagInstruction(
        bool isCharacter,
        RagQueryPlan plan,
        string userMessage,
        RagConversationMode conversationMode)
    {
        if (plan.Intent == RagQueryIntent.Article)
            return false;

        if (conversationMode is RagConversationMode.SoftTopic or RagConversationMode.RiskCaution)
            return true;

        if (!isCharacter)
            return false;

        if (plan.ResponseMode == RagResponseMode.PersonaSynthesis
            || plan.Intent == RagQueryIntent.Advisory)
            return true;

        return !RagFormalLegalCue.IsFormalLegalQuery(userMessage);
    }
}
