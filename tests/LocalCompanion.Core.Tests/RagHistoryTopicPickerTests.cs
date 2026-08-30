using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class RagHistoryTopicPickerTests
{
    [Fact]
    public void PickPreviousUserTopic_SkipsAckThenTakesLegal()
    {
        var rows = new (string Role, string Content)[]
        {
            ("user", "そうなんだ"),
            ("assistant", "第4条です"),
            ("user", "刑法4条はなーんだ？"),
        };

        var picked = RagHistoryTopicPicker.PickPreviousUserTopic(rows);
        Assert.Equal("刑法4条はなーんだ？", picked);
    }

    [Fact]
    public void IsTopiclessAck_DoesNotTreatArticleQueryAsAck()
    {
        Assert.False(RagHistoryTopicPicker.IsTopiclessAck("4条って何？"));
        Assert.True(RagHistoryTopicPicker.IsTopiclessAck("そうなんだ"));
    }
}
