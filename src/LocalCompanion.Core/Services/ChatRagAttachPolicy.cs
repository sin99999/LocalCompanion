namespace LocalCompanion.Services;

/// <summary>
/// チャット1通で RAG を走らせてよいか。画像はコンテキスト予算側で縮める（ここでは止めない）。
/// </summary>
internal static class ChatRagAttachPolicy
{
    public static bool Allow(
        bool useRag,
        string effectiveMessage,
        string? attachedText,
        int ragChunkCount,
        int lightAttachMaxChars)
    {
        if (!useRag || ragChunkCount <= 0)
            return false;
        if (effectiveMessage.Length < 4)
            return false;
        if (!string.IsNullOrWhiteSpace(attachedText)
            && attachedText.Length > lightAttachMaxChars
            && !PreferRagDespiteLongAttach(effectiveMessage))
            return false;
        return true;
    }

    /// <summary>長文添付（Web 本文など）でも、法令・条文クエリは RAG を落とさない。</summary>
    internal static bool PreferRagDespiteLongAttach(string effectiveMessage)
    {
        if (RagArticleQueryParser.TryGetArticleNumber(effectiveMessage, out _))
            return true;
        if (RagLegalQueryContext.LooksLikeLegalArticleQuery(effectiveMessage, sourceHint: null))
            return true;
        return ChatAgentResearchEnricher.LooksLikeLocalCorpusResearch(effectiveMessage);
    }
}
