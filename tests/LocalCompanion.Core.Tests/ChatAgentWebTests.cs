using System.Net;
using LocalCompanion.Localization;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatMessageUrlExtractorTests
{
    [Fact]
    public void Extract_FindsHttpAndHttps_DedupesAndTrimsPunctuation()
    {
        var urls = ChatMessageUrlExtractor.Extract(
            "見て https://example.com/a。 と https://example.com/a と https://example.org/b）");

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://example.com/a", urls[0]);
        Assert.Equal("https://example.org/b", urls[1]);
    }

    [Fact]
    public void ResolveUrlAtIndex_FindsUrlUnderCaret()
    {
        const string text = "参考:\r\nhttps://example.com/page\r\n終わり";
        var urlStart = text.IndexOf("https://", StringComparison.Ordinal);
        Assert.True(urlStart >= 0);

        Assert.Equal(
            "https://example.com/page",
            ChatMessageUrlExtractor.ResolveUrlAtIndex(text, urlStart + 5));
        Assert.Equal(
            "https://example.com/page",
            ChatMessageUrlExtractor.ResolveUrlAtIndex(text, urlStart + "https://example.com/page".Length));
        Assert.Null(ChatMessageUrlExtractor.ResolveUrlAtIndex(text, 0));
    }

    [Fact]
    public void Extract_FromCitationBlock_ReturnsUrls()
    {
        var text = """
            量子の説明です。

            参考:
            https://example.com/a
            https://example.org/b
            """;

        var urls = ChatMessageUrlExtractor.Extract(text, maxCount: 16);
        Assert.Equal(2, urls.Count);
        Assert.Equal("https://example.com/a", urls[0]);
        Assert.Equal("https://example.org/b", urls[1]);
    }

    [Fact]
    public void SplitByUrls_KeepsPlainTextAndUrlSegmentsInOrder()
    {
        var segments = ChatMessageUrlExtractor.SplitByUrls(
            "前 https://example.com/a 中 https://example.org/b。");

        Assert.Equal(5, segments.Count);
        Assert.False(segments[0].IsUrl);
        Assert.Equal("前 ", segments[0].Text);
        Assert.True(segments[1].IsUrl);
        Assert.Equal("https://example.com/a", segments[1].Text);
        Assert.False(segments[2].IsUrl);
        Assert.Equal(" 中 ", segments[2].Text);
        Assert.True(segments[3].IsUrl);
        Assert.Equal("https://example.org/b", segments[3].Text);
        Assert.False(segments[4].IsUrl);
        Assert.Equal("。", segments[4].Text);
    }

    [Fact]
    public void Extract_RespectsMaxCount()
    {
        var urls = ChatMessageUrlExtractor.Extract(
            "https://a.example/ https://b.example/ https://c.example/",
            maxCount: 2);
        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public void Extract_StopsBeforeTrailingJapaneseWithoutSpace()
    {
        var urls = ChatMessageUrlExtractor.Extract(
            "https://github.com/sin99999/LocalCompanionだよ？");

        Assert.Single(urls);
        Assert.Equal("https://github.com/sin99999/LocalCompanion", urls[0]);
    }

    [Fact]
    public void SanitizeUrlMatch_RemovesFullwidthQuestionGlue()
    {
        Assert.Equal(
            "https://example.com/path",
            ChatMessageUrlExtractor.SanitizeUrlMatch("https://example.com/pathこれだよ？"));
    }

    [Fact]
    public void SplitByUrls_KeepsJapaneseAfterUrlAsPlainText()
    {
        var segments = ChatMessageUrlExtractor.SplitByUrls(
            "https://example.com/aだよ？続き");

        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].IsUrl);
        Assert.Equal("https://example.com/a", segments[0].Text);
        Assert.False(segments[1].IsUrl);
        Assert.Equal("だよ？続き", segments[1].Text);
    }
}

public sealed class ChatUrlHostGuardTests
{
    [Theory]
    [InlineData("http://localhost/x")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("http://metadata.google.internal/")]
    public void IsBlocked_PrivateAndMetadata_True(string url)
    {
        Assert.True(ChatUrlHostGuard.IsBlocked(new Uri(url)));
    }

    [Fact]
    public void IsBlockedAddress_LoopbackAndPrivate()
    {
        Assert.True(ChatUrlHostGuard.IsBlockedAddress(IPAddress.Loopback));
        Assert.True(ChatUrlHostGuard.IsBlockedAddress(IPAddress.Parse("10.1.2.3")));
        Assert.False(ChatUrlHostGuard.IsBlockedAddress(IPAddress.Parse("8.8.8.8")));
    }
}

public sealed class ChatUrlContentFetcherHostTests
{
    [Theory]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://localhost/x")]
    [InlineData("http://192.168.0.10/")]
    [InlineData("http://10.1.2.3/")]
    public async Task FetchAsync_RejectsBlockedHostsBeforeDownload(string url)
    {
        var ex = await Assert.ThrowsAsync<LocalizedServiceException>(
            () => ChatUrlContentFetcher.FetchAsync(url));
        Assert.Equal("Chat.Url.HostNotAllowed", ex.LocalizationKey);
    }
}

public sealed class ChatWebSearchClientTests
{
    [Fact]
    public void ParseDuckDuckGoHtml_ExtractsHitsAndUnwrapsRedirect()
    {
        const string html = """
            <div class="result">
            <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fpage">Example Title</a>
            <a class="result__snippet">Snippet one</a>
            </div>
            <div class="result">
            <a class="result__a" href="https://example.org/other">Other</a>
            <a class="result__snippet">Snippet two</a>
            </div>
            """;

        var hits = ChatWebSearchClient.ParseDuckDuckGoHtml(html, topK: 5);
        Assert.Equal(2, hits.Count);
        Assert.Equal("https://example.com/page", hits[0].Url);
        Assert.Equal("Example Title", hits[0].Title);
        Assert.Equal("https://example.org/other", hits[1].Url);
    }

    [Fact]
    public void UnwrapDuckDuckGoRedirect_PassthroughForNormalUrl()
    {
        Assert.Equal(
            "https://example.com/",
            ChatWebSearchClient.UnwrapDuckDuckGoRedirect("https://example.com/"));
    }
}

public sealed class ChatAgentResearchEnricherTests
{
    [Theory]
    [InlineData("量子コンピュータについて調べて", true)]
    [InlineData("search the web for quantum", true)]
    [InlineData("こんにちは", false)]
    [InlineData("ネットワークが切れた", false)]
    [InlineData("インターネット回線が遅い", false)]
    [InlineData("RAGの資料から検索して、どんな刑に相当するか提示する仕組みのテスト", false)]
    [InlineData("登録資料を検索して第11条を出して", false)]
    [InlineData("刑法235条について調べて", false)]
    [InlineData("国外犯を調べて", false)]
    [InlineData("労基法第37条を検索して", false)]
    [InlineData("FTLとは調べて", false)]
    [InlineData("就業規則を調べて", false)]
    [InlineData("ウェブでFTLとは調べて", true)]
    [InlineData("ウェブで最新の為替を調べて", true)]
    [InlineData("ネットで天気を検索して", true)]
    [InlineData("ネットからフェルンを探してプロンプトを教えて", true)]
    [InlineData("ネットやらからフェルンを探してプロンプトを教えてくれない？", true)]
    [InlineData("Stable Diffusionで葬送のフリーレンのフェルンを表現したい。ネットやらからフェルンを探してプロンプトを教えてくれない？", true)]
    [InlineData("インターネットから今日の天気を教えて", true)]
    [InlineData("最新情報の刑法4条を調べて", false)]
    [InlineData("それを調べて", false)]
    public void LooksLikeResearchIntent_DetectsCues(string message, bool expected)
    {
        Assert.Equal(expected, ChatAgentResearchEnricher.LooksLikeResearchIntent(message));
    }

    [Theory]
    [InlineData("それを調べて", "刑法235条は？", false)]
    [InlineData("もっと詳しく調べて", "刑法235条の罰則は？", false)]
    [InlineData("最新情報を調べて", "刑法235条は？", false)]
    [InlineData("それを調べて", "今日の東京の天気は？", true)]
    [InlineData("最新情報を調べて", "今日の東京の天気は？", true)]
    public void LooksLikeResearchIntent_UsesPreviousTurnForLocalVsWeb(
        string message,
        string previous,
        bool expectedWeb)
    {
        Assert.Equal(
            expectedWeb,
            ChatAgentResearchEnricher.LooksLikeResearchIntent(message, previous));
    }

    [Fact]
    public void BuildSearchQuery_StripsCueAndExportTail()
    {
        var q = ChatAgentResearchEnricher.BuildSearchQuery("刑法の罰金について調べてデスクトップに置いて");
        Assert.DoesNotContain("調べて", q);
        Assert.DoesNotContain("デスクトップ", q);
        Assert.Contains("刑法", q);
    }

    [Fact]
    public void BuildSearchQuery_StripsColloquialNetCue()
    {
        var q = ChatAgentResearchEnricher.BuildSearchQuery(
            "ネットやらからフェルンを探してプロンプトを教えてくれない？");
        Assert.DoesNotContain("ネットやら", q, StringComparison.Ordinal);
        Assert.Contains("フェルン", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchQuery_FollowUpPronoun_IncludesPriorTopic()
    {
        var q = ChatAgentResearchEnricher.BuildSearchQuery("それを調べて", "今日の東京の天気は？");
        Assert.Contains("天気", q);
        Assert.DoesNotContain("調べて", q);
    }

    [Fact]
    public void FormatSearchAttachment_LeadsWithReferenceUrlList()
    {
        var hits = new[]
        {
            new ChatWebSearchHit("Example A", "https://example.com/a", "snip a"),
            new ChatWebSearchHit("Example B", "https://example.org/b", "snip b"),
        };
        var pages = new[]
        {
            ("https://example.com/a", "Example A", "body of a"),
        };

        var text = ChatAgentResearchEnricher.FormatSearchAttachment("quantum", hits, pages);

        Assert.Contains("【参考URL】", text);
        var sourcesIdx = text.IndexOf("【参考URL】", StringComparison.Ordinal);
        var bodyIdx = text.IndexOf("body of a", StringComparison.Ordinal);
        Assert.True(sourcesIdx >= 0 && bodyIdx > sourcesIdx);
        Assert.Contains("https://example.com/a", text);
        Assert.Contains("https://example.org/b", text);
        Assert.Contains("URL: https://example.com/a", text);
    }
}

public sealed class ChatWebSourceCitationTests
{
    [Fact]
    public void AppendIfMissing_AddsSourcesBlockWhenReplyHasNoUrls()
    {
        var reply = ChatWebSourceCitation.AppendIfMissing(
            "量子の話です。",
            ["https://example.com/a", "https://example.org/b"],
            japanese: true);

        Assert.Contains("参考:", reply);
        Assert.Contains("https://example.com/a", reply);
        Assert.Contains("https://example.org/b", reply);
    }

    [Fact]
    public void AppendIfMissing_SkipsWhenAllUrlsAlreadyPresent()
    {
        const string original = "詳細は https://example.com/a を見て。";
        var reply = ChatWebSourceCitation.AppendIfMissing(
            original,
            ["https://example.com/a"],
            japanese: true);

        Assert.Equal(original, reply);
    }

    [Fact]
    public void Merge_DedupesPreserveOrder()
    {
        var merged = ChatWebSourceCitation.Merge(
            ["https://example.com/a", "https://example.org/b"],
            ["https://example.com/a", "https://example.net/c"]);

        Assert.Equal(
            new[] { "https://example.com/a", "https://example.org/b", "https://example.net/c" },
            merged);
    }
}

public sealed class ChatAgentToolCallParserTests
{
    [Fact]
    public void TryParse_ToolJsonBlock_Succeeds()
    {
        var ok = ChatAgentToolCallParser.TryParse(
            """
            thinking...
            ```tool
            {"name":"web_search","args":{"query":"hello"}}
            ```
            """,
            out var name,
            out var args);

        Assert.True(ok);
        Assert.Equal("web_search", name);
        Assert.Equal("hello", args["query"]);
    }
}
