using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LocalCompanion.Controls;

/// <summary>テキスト選択中に ScrollViewer を端方向へ自動スクロールする。</summary>
internal static class SelectionAutoScrollHelper
{
    private const int VkLButton = 0x01;
    private const double EdgePixels = 72;
    private const double StepPixels = 36;
    private const double MaxSpeedMultiplier = 8;

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

    public static void ScrollIfNeeded(ScrollViewer scrollHost, nint windowHandle)
    {
        if (scrollHost.ActualHeight <= 0 || scrollHost.ScrollableHeight <= 0)
            return;

        if (!TryGetCursorYInElement(scrollHost, windowHandle, out var pointerY))
            return;

        var viewportHeight = scrollHost.ActualHeight;
        double delta = 0;

        // ビューポート外（上／下）に出ても端スクロールを継続
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

    private static bool TryGetCursorYInElement(FrameworkElement element, nint windowHandle, out double y)
    {
        y = 0;
        if (windowHandle == 0)
            return false;

        if (element.XamlRoot?.Content is not UIElement root)
            return false;

        if (!GetCursorPos(out var screen))
            return false;

        var client = new Point { X = screen.X, Y = screen.Y };
        if (!ScreenToClient(windowHandle, ref client))
            return false;

        // HWND クライアント座標 ≈ XamlRoot.Content 座標 → 要素ローカルへ
        var transform = root.TransformToVisual(element);
        y = transform.TransformPoint(new Windows.Foundation.Point(client.X, client.Y)).Y;
        return true;
    }
}
