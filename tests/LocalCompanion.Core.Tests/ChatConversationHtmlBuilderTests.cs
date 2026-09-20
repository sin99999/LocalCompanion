using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatConversationHtmlBuilderTests
{
    [Fact]
    public void BuildShell_IncludesCopyLabels()
    {
        var html = ChatConversationHtmlBuilder.BuildShell("Segoe UI", 14, "Copy", "Copied");
        Assert.Contains("data-copy-label=\"Copy\"", html, StringComparison.Ordinal);
        Assert.Contains("data-copied-label=\"Copied\"", html, StringComparison.Ordinal);
        Assert.Contains("lcCopyBlock", html, StringComparison.Ordinal);
        Assert.Contains("lcSetCopyLabels", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildShell_ProseUsesKinsokuFriendlyWrap()
    {
        var html = ChatConversationHtmlBuilder.BuildShell("Segoe UI", 14, "Copy", "Copied");
        Assert.Contains("overflow-wrap: break-word;", html, StringComparison.Ordinal);
        Assert.Contains("line-break: strict;", html, StringComparison.Ordinal);
        var bodyCssStart = html.IndexOf("html, body {", StringComparison.Ordinal);
        Assert.True(bodyCssStart >= 0);
        var bodyCssEnd = html.IndexOf('}', bodyCssStart);
        var bodyCss = html[bodyCssStart..bodyCssEnd];
        Assert.DoesNotContain("overflow-wrap: anywhere;", bodyCss, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBody_UserHalfWidthParenAside_StaysOneParagraph()
    {
        const string body =
            "着物を着た女子高生(氷菓のエルたそみたいな？)と雪の積もる初詣に出かけるシチュをSDプロンプトで出力して？";
        var html = ChatConversationHtmlBuilder.BuildBodyWithLinks(body, sentenceBreaks: false);
        Assert.Contains(body, html, StringComparison.Ordinal);
        Assert.Equal(1, CountTag(html, "<p>"));
        Assert.DoesNotContain("生(<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">)と", html, StringComparison.Ordinal);
    }

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
            new ChatConversationHtmlBuilder.Line("Assistant", "step one", "final answer", true, "推論", ShowReasoningPanel: true),
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
    public void BuildLogHtml_CompletedHidesReasoningWhenPanelOff()
    {
        var html = ChatConversationHtmlBuilder.BuildLogHtml(
        [
            new ChatConversationHtmlBuilder.Line("Assistant", "step one", "final answer", true, "推論"),
        ]);
        Assert.DoesNotContain("class=\"reasoning\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("step one", html, StringComparison.Ordinal);
        Assert.Contains("final answer", html, StringComparison.Ordinal);
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
        Assert.Contains("<ul class=\"chat-list\">", live, StringComparison.Ordinal);
        Assert.Contains("chat-table", live, StringComparison.Ordinal);

        var done = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, body, true, LiveStream: false));
        Assert.Contains("<ul class=\"chat-list\">", done, StringComparison.Ordinal);
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
        Assert.Contains("<ul class=\"chat-list\">", html, StringComparison.Ordinal);
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
    public void BuildArticleHtml_Completed_NumberedList_PutsMarkerInText()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line(
                "Assistant",
                null,
                "1. りんご\n2. みかん",
                true));

        Assert.Contains("<ol class=\"chat-list\">", html, StringComparison.Ordinal);
        Assert.Contains("class=\"list-mark\">・1. ", html, StringComparison.Ordinal);
        Assert.Contains("class=\"list-mark\">・2. ", html, StringComparison.Ordinal);
        Assert.Contains("りんご", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBody_ExpandedChoiceThree_KeepsNumberThree()
    {
        const string assistant = """
            1. 他にも気になる罪があるか調べてみる？
            2. この罰則の内容をもっと詳しく深掘りする？
            3. とにかく今の話を続けて盛り上がる！
            """;

        var expanded = ChatChoiceReplyExpander.Expand("3", assistant);
        Assert.Equal("3. とにかく今の話を続けて盛り上がる！", expanded);

        var html = ChatConversationHtmlBuilder.BuildBodyWithLinks(expanded, sentenceBreaks: false);
        Assert.Contains("class=\"list-mark\">3. ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"list-mark\">1. ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBody_JammedNumberedChoices_ShowsBulletLines()
    {
        const string body =
            "選んでみて！✨*【選択肢】1.**ギャルトーク全開！\n"
            + "（最近の流行りとか、うちの話をたっぷり！）2.甘えん坊タイム♡（ちょっと二人きりで、じっくりイチャイチャ？）3.日常の雑談♪（今日あったこととか、ゆるく話そ！）【レンのおすすめ】やっぱり……「甘えん坊タイム」**かな！";

        var html = ChatConversationHtmlBuilder.BuildBodyWithLinks(body, sentenceBreaks: true);
        Assert.Contains("class=\"list-mark\">・", html, StringComparison.Ordinal);
        Assert.Contains("ギャルトーク全開", html, StringComparison.Ordinal);
        Assert.Contains("甘えん坊タイム", html, StringComparison.Ordinal);
        Assert.Contains("日常の雑談", html, StringComparison.Ordinal);
        Assert.DoesNotContain("）2.甘えん坊", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_Completed_FencedCode_HasCopyButton()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, "```\nhello\n```", true));

        Assert.Contains("chat-code-wrap", html, StringComparison.Ordinal);
        Assert.Contains("class=\"chat-copy\"", html, StringComparison.Ordinal);
        Assert.Contains("chat-copy-icon", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">コピー</button>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"chat-code\"", html, StringComparison.Ordinal);
        Assert.Contains("hello", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_Completed_PromptBlob_HasCopyButton()
    {
        const string body =
            "(Masterpiece, Bestquality,Highres:1.2),(Anime style:1.3),1girl, nagato yuki, "
            + "defensive pose, looking at camera, determined expression, serious face, "
            + "short navy blue hair, purple eyes, floating in void, cosmic background";

        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, body, true));

        Assert.Contains("chat-code-wrap", html, StringComparison.Ordinal);
        Assert.Contains("class=\"chat-copy\"", html, StringComparison.Ordinal);
        Assert.Contains("Masterpiece", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArticleHtml_Completed_RendersInlineMarkup()
    {
        var html = ChatConversationHtmlBuilder.BuildArticleHtml(
            new ChatConversationHtmlBuilder.Line("Assistant", null, "これは **大事** と `x` です", true));
        Assert.Contains("<strong>大事</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<code>x</code>", html, StringComparison.Ordinal);
    }

    private static int CountTag(string html, string tag)
    {
        var n = 0;
        for (var i = 0; (i = html.IndexOf(tag, i, StringComparison.Ordinal)) >= 0; i += tag.Length)
            n++;
        return n;
    }
}
