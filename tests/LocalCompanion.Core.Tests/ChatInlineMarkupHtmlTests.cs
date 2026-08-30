using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatInlineMarkupHtmlTests
{
    [Fact]
    public void Format_Bold_WrapsStrong()
    {
        var html = ChatInlineMarkupHtml.Format("これは **大事** だよ");
        Assert.Contains("<strong>大事</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("**大事**", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_InlineCode_WrapsCodeAndEscapes()
    {
        var html = ChatInlineMarkupHtml.Format("型は `List<T>` です");
        Assert.Contains("<code>", html, StringComparison.Ordinal);
        Assert.Contains("List&lt;T&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<T>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_MarkdownLink_HttpOnly()
    {
        var html = ChatInlineMarkupHtml.Format("見て [例](https://example.com/a) ね");
        Assert.Contains("<a href=\"https://example.com/a\">", html, StringComparison.Ordinal);
        Assert.Contains(">例</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_MarkdownLink_RejectsJavascript()
    {
        var html = ChatInlineMarkupHtml.Format("[x](javascript:alert(1))");
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
        Assert.Contains("javascript:alert(1)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_EncodesRawHtml()
    {
        var html = ChatInlineMarkupHtml.Format("<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_CodeWinsOverBold()
    {
        var html = ChatInlineMarkupHtml.Format("`**x**`");
        Assert.Contains("<code>", html, StringComparison.Ordinal);
        Assert.Contains("**x**", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_BareHttpUrl_StillLinks()
    {
        var html = ChatInlineMarkupHtml.Format("see https://example.com/a ok");
        Assert.Contains("<a href=\"https://example.com/a\">", html, StringComparison.Ordinal);
    }
}
