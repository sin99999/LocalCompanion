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
}
