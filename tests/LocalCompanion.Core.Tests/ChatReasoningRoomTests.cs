using LocalCompanion.Services;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class ChatReasoningRoomTests
{
    [Fact]
    public void RequestedOutputTokens_LeavesRoomAfterTheReasoningBudget()
    {
        Assert.Equal(1024, ChatService.RequestedOutputTokens(false, 1024));
        Assert.Equal(1024 + LlamaServerNativeHost.GemmaReasoningBudgetTokens, ChatService.RequestedOutputTokens(true, 1024));
    }

    [Fact]
    public void IsUnparsedChatFormat_MatchesTheGemmaParserError()
    {
        Assert.True(LlamaServerClient.IsUnparsedChatFormat(
            "The model produced output that does not match the expected peg-gemma4 format"));
        Assert.False(LlamaServerClient.IsUnparsedChatFormat("exceed_context_size"));
        Assert.False(LlamaServerClient.IsUnparsedChatFormat(null));
    }
}
