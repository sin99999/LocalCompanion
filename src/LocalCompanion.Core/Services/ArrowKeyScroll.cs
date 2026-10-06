namespace LocalCompanion.Services;

internal static class ArrowKeyScroll
{
    public const double StepPixels = 64;

    public static bool TryStep(double offset, double scrollableHeight, bool upward, out double next)
    {
        next = offset;
        if (scrollableHeight <= 0 || StepPixels <= 0)
            return false;

        var target = upward
            ? offset - StepPixels
            : offset + StepPixels;
        if (target < 0)
            target = 0;
        if (target > scrollableHeight)
            target = scrollableHeight;
        if (Math.Abs(target - offset) < 0.5)
            return false;

        next = target;
        return true;
    }
}
