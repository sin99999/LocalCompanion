using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 条番号の法令質問を LLM に渡すと、別条を「関連」として混ぜることがある。
/// キャラクター中でも機械引用（LLM を通さない）に回すかの判定だけを持つ。指示の選び方は <see cref="RagAnswerPolicy"/>。
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
}
