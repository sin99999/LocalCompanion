using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatRichTextDisplayNormalizerTests
{
    [Fact]
    public void Normalize_StripsEmojiVariationSelector()
    {
        // ❤️ = U+2764 U+FE0F
        var input = "感じは\u2764\uFE0Fるかな？";
        var normalized = ChatRichTextDisplayNormalizer.Normalize(input);
        Assert.Equal("感じは\u2764るかな？", normalized);
        Assert.DoesNotContain('\uFE0F', normalized);
    }

    [Fact]
    public void Normalize_LeavesPlainText()
    {
        Assert.Equal("hello", ChatRichTextDisplayNormalizer.Normalize("hello"));
    }

    [Fact]
    public void Normalize_StripsNul()
    {
        Assert.Equal("ab", ChatRichTextDisplayNormalizer.Normalize("a\0b"));
    }

    [Fact]
    public void Normalize_NullOrEmpty()
    {
        Assert.Equal(string.Empty, ChatRichTextDisplayNormalizer.Normalize(null));
        Assert.Equal(string.Empty, ChatRichTextDisplayNormalizer.Normalize(string.Empty));
    }
}
