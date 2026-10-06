namespace LocalCompanion.Services;

internal enum JuliaRouteChoice
{
    Keep,
    Web,
    Notes,
    Chat,
}

/// <summary>
/// 合図がはっきりした発話は今の係のまま。迷った発話だけ Julia の web / notes / chat を載せる。
/// </summary>
internal static class JuliaRoutePolicy
{
    public static bool ShouldAsk(string? message, string? previousUserMessage)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        if (ChatAppearanceLookup.LooksLikeAppearanceRequest(message))
            return false;
        if (ChatMessageUrlExtractor.Extract(message, 1).Count > 0)
            return false;
        if (ChatAgentResearchEnricher.IsExplicitWebRequest(message))
            return false;

        var plan = RagQueryPlanner.Plan(message, previousUserMessage);
        var mode = RagConversationGate.Resolve(plan, message);
        if (mode is RagConversationMode.Skip or RagConversationMode.Structured or RagConversationMode.RiskCaution)
            return false;
        if (ChatAgentResearchEnricher.LooksLikeLocalCorpusResearch(message, previousUserMessage))
            return false;
        if (!ChatAgentResearchEnricher.LooksLikeResearchIntent(message, previousUserMessage))
            return false;

        return mode is RagConversationMode.Probe or RagConversationMode.SoftTopic;
    }

    public static JuliaRouteChoice ChoiceFromLabel(string? label, double confidence)
    {
        if (string.IsNullOrWhiteSpace(label) || confidence < LayaDecision.SearchConfidence)
            return JuliaRouteChoice.Keep;

        if (label.Equals("web", StringComparison.OrdinalIgnoreCase))
            return JuliaRouteChoice.Web;
        if (label.Equals("notes", StringComparison.OrdinalIgnoreCase))
            return JuliaRouteChoice.Notes;
        if (label.Equals("chat", StringComparison.OrdinalIgnoreCase))
            return JuliaRouteChoice.Chat;
        return JuliaRouteChoice.Keep;
    }

    public static RagConversationMode ApplyToNotes(RagConversationMode mode, JuliaRouteChoice choice, bool asked)
    {
        if (!asked || choice == JuliaRouteChoice.Keep)
            return mode;
        if (choice == JuliaRouteChoice.Notes && mode is RagConversationMode.Probe or RagConversationMode.SoftTopic)
            return RagConversationMode.SoftTopic;
        if (choice is JuliaRouteChoice.Web or JuliaRouteChoice.Chat && mode == RagConversationMode.SoftTopic)
            return RagConversationMode.Probe;
        return mode;
    }

    /// <summary>null は今の合図語。true はウェブへ。false はウェブに出さない。</summary>
    public static bool? WebOverride(JuliaRouteChoice choice, bool asked)
    {
        if (!asked || choice == JuliaRouteChoice.Keep)
            return null;
        return choice == JuliaRouteChoice.Web;
    }
}
