using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public class ArrowKeyScrollTests
{
    [Fact]
    public void Up_FromTheTop_Stays()
    {
        var moved = ArrowKeyScroll.TryStep(0, 400, upward: true, out var next);

        Assert.False(moved);
        Assert.Equal(0, next);
    }

    [Fact]
    public void Down_MovesOneStep_ThenStopsAtTheEnd()
    {
        Assert.True(ArrowKeyScroll.TryStep(0, 400, upward: false, out var next));
        Assert.Equal(ArrowKeyScroll.StepPixels, next);

        Assert.True(ArrowKeyScroll.TryStep(380, 400, upward: false, out var clamped));
        Assert.Equal(400, clamped);

        Assert.False(ArrowKeyScroll.TryStep(400, 400, upward: false, out var stayed));
        Assert.Equal(400, stayed);
    }

    [Fact]
    public void NothingToScroll_DoesNotMove()
    {
        Assert.False(ArrowKeyScroll.TryStep(0, 0, upward: false, out var next));
        Assert.Equal(0, next);
    }
}
