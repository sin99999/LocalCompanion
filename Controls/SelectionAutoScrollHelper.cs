using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.Controls;

/// <summary>TextBlock 選択中に ScrollViewer を端方向へ自動スクロールする。</summary>
internal static class SelectionAutoScrollHelper
{
    private const int VkLButton = 0x01;
    private const double EdgePixels = 56;
    private const double StepPixels = 28;
    private const double MaxSpeedMultiplier = 6;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point screen);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hWnd, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    public static bool IsLeftButtonPressed() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    public static void ScrollIfNeeded(ScrollViewer scrollHost, UIElement coordinateRoot, nint windowHandle)
    {
        if (scrollHost.ActualHeight <= 0 || scrollHost.ScrollableHeight <= 0)
            return;

        if (!TryGetCursorYInElement(scrollHost, coordinateRoot, windowHandle, out var pointerY))
            return;

        var viewportHeight = scrollHost.ActualHeight;
        double delta = 0;

        if (pointerY < EdgePixels)
        {
            var depth = EdgePixels - pointerY;
            var factor = Math.Clamp(depth / EdgePixels, 0.75, MaxSpeedMultiplier);
            delta = -StepPixels * factor;
        }
        else if (pointerY > viewportHeight - EdgePixels)
        {
            var depth = pointerY - (viewportHeight - EdgePixels);
            var factor = Math.Clamp(depth / EdgePixels, 0.75, MaxSpeedMultiplier);
            delta = StepPixels * factor;
        }

        if (Math.Abs(delta) < 0.5)
            return;

        var target = Math.Clamp(scrollHost.VerticalOffset + delta, 0, scrollHost.ScrollableHeight);
        if (Math.Abs(target - scrollHost.VerticalOffset) < 0.5)
            return;

        scrollHost.ChangeView(null, target, null, disableAnimation: true);
    }

    private static bool TryGetCursorYInElement(
        FrameworkElement element,
        UIElement coordinateRoot,
        nint windowHandle,
        out double y)
    {
        y = 0;
        if (windowHandle == 0)
            return false;

        if (!GetCursorPos(out var screen))
            return false;

        var client = new Point { X = screen.X, Y = screen.Y };
        if (!ScreenToClient(windowHandle, ref client))
            return false;

        var windowPoint = new Windows.Foundation.Point(client.X, client.Y);
        var transform = element.TransformToVisual(coordinateRoot);
        if (transform.Inverse is not GeneralTransform inverse)
            return false;

        y = inverse.TransformPoint(windowPoint).Y;
        return true;
    }
}
