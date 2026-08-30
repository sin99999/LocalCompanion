using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatConversationHtmlBuilderTests
{
    [Fact]
    public void LinkifyEscaped_WrapsHttpUrl()
    {
        var html = ChatConversationHtmlBuilder.LinkifyEscaped("see https://example.com/a ok");
        Assert.Contains("<a href=\"https://example.com/a\">", html, StringComparison.Ordinal);
        Assert.Contains("https://example.com/a</a>", html, StringComparison.Ordinal);
        Assert.Contains("see ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkifyEscaped_EncodesHtml()
    {
        var html = ChatConversationHtmlBuilder.LinkifyEscaped("<b>x</b>");
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLogHtml_IncludesHeaderAndBody()
    {
        var html = ChatConversationHtmlBuilder.BuildLogHtml(
        [
            new ChatConversationHtmlBuilder.Line("User", null, "hello https://example.com", false),
        ]);
        Assert.Contains("User", html, StringComparison.Ordinal);
        Assert.Contains("hello", html, StringComparison.Ordinal);
        // AbsoluteUri はホストのみだと末尾 / が付くことがある
        Assert.Contains("<a href=\"https://example.com", html, StringComparison.Ordinal);
        Assert.Contains("</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLogHtml_IncludesReasoningBlockWithLabel()
    {
        var html = ChatConversationHtmlBuilder.BuildLogHtml(
        [
            new ChatConversationHtmlBuilder.Line("Assistant", "step one", "final answer", true, "推論"),
        ]);
        Assert.Contains("class=\"reasoning\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"reasoning-label\"", html, StringComparison.Ordinal);
        Assert.Contains("step one", html, StringComparison.Ordinal);
        Assert.Contains("final answer", html, StringComparison.Ordinal);
        // 推論が本文より前
        Assert.True(
            html.IndexOf("step one", StringComparison.Ordinal)
            < html.IndexOf("final answer", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArticleHtml_LiveStream_ShowsPlainTextAndCaret()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line(
                "Assistant",
                "thinking now",
                "",
                true,
                "推論",
                LiveStream: true,
                ShowReasoningPanel: true));

        Assert.Contains("reasoning live", html, StringComparison.Ordinal);
        Assert.Contains("stream-caret", html, StringComparison.Ordinal);
        Assert.Contains("thinking now", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_Completed_RendersListAndTable()
    {
        const string body = """
            - りんご
            - みかん

            | 名前 | 数 |
            | --- | --- |
            | a | 1 |
            """;

        var live = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, body, true, LiveStream: true));
        // リストは後ろに表があるので閉じ済み。表は行があるので閉じ済み
        Assert.Contains("<ul>", live, StringComparison.Ordinal);
        Assert.Contains("chat-table", live, StringComparison.Ordinal);

        var done = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, body, true, LiveStream: false));
        Assert.Contains("<ul>", done, StringComparison.Ordinal);
        Assert.Contains("chat-table", done, StringComparison.Ordinal);
        Assert.DoesNotContain("stream-caret", done, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_LiveStream_OnlyList_StaysPlain()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, "- りんご\n- みかん", true, LiveStream: true));
        Assert.DoesNotContain("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("りんご", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_LiveStream_ClosedListThenOpenTable_RendersListOnly()
    {
        const string body = """
            - りんご
            - みかん

            | 名前 | 数 |
            | --- | --- |
            """;

        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, body, true, LiveStream: true));
        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("chat-table", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_LiveStream_UnclosedFence_IsNotCodeBlock()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, "```\nhello", true, LiveStream: true));
        Assert.DoesNotContain("chat-code", html, StringComparison.Ordinal);
        Assert.Contains("hello", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_Completed_RendersInlineMarkup()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, "これは **大事** と `x` です", true));
        Assert.Contains("<strong>大事</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<code>x</code>", html, StringComparison.Ordinal);
    }
}
