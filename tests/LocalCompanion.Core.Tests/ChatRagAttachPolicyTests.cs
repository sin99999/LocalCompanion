using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatRagAttachPolicyTests
{
    private const int LightMax = 3000;

    [Fact]
    public void Allow_ArticleQuestion_IsTrueEvenWhenCallerAlsoHasImage()
    {
        // 画像の有無はポリシーに渡さない（旧 ChatService の !hasImage が条文を止めていた）
        Assert.True(ChatRagAttachPolicy.Allow(
            useRag: true,
            effectiveMessage: "刑法4条はなーんだ？",
            attachedText: null,
            ragChunkCount: 12,
            lightAttachMaxChars: LightMax));
    }

    [Fact]
    public void Allow_LongTextAttachment_NonLegal_IsFalse()
    {
        Assert.False(ChatRagAttachPolicy.Allow(
            useRag: true,
            effectiveMessage: "量子コンピュータについて教えて",
            attachedText: new string('あ', LightMax + 1),
            ragChunkCount: 12,
            lightAttachMaxChars: LightMax));
    }

    [Fact]
    public void Allow_LongTextAttachment_LegalArticle_IsTrue()
    {
        // Web 本文が長くても、条文クエリは RAG を落とさない
        Assert.True(ChatRagAttachPolicy.Allow(
            useRag: true,
            effectiveMessage: "刑法4条はなーんだ？",
            attachedText: new string('あ', LightMax + 1),
            ragChunkCount: 12,
            lightAttachMaxChars: LightMax));
    }

    [Fact]
    public void Allow_RagOffOrEmptyIndexOrShortMessage_IsFalse()
    {
        Assert.False(ChatRagAttachPolicy.Allow(false, "刑法4条はなーんだ？", null, 12, LightMax));
        Assert.False(ChatRagAttachPolicy.Allow(true, "刑法4条はなーんだ？", null, 0, LightMax));
        Assert.False(ChatRagAttachPolicy.Allow(true, "あいう", null, 12, LightMax));
    }

    [Fact]
    public void Allow_LightTextAttachment_IsTrue()
    {
        Assert.True(ChatRagAttachPolicy.Allow(
            useRag: true,
            effectiveMessage: "この資料の第4条は？",
            attachedText: "短いメモ",
            ragChunkCount: 3,
            lightAttachMaxChars: LightMax));
    }
}
