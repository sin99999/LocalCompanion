using LocalCompanion.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace LocalCompanion.Controls;

/// <summary>設定の各タブで、上下キーが欄の操作に要らないとき、そのタブの中身をスクロールする。</summary>
internal static class SettingsTabArrowScroll
{
    public static void Handle(KeyRoutedEventArgs e, ScrollViewer? tabScroll, DependencyObject? focused)
    {
        if (e.Handled)
            return;
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down))
            return;
        if (IsChord())
            return;
        if (KeepsVerticalArrows(focused))
            return;
        if (tabScroll is null)
            return;

        var upward = e.Key == VirtualKey.Up;
        if (!ArrowKeyScroll.TryStep(tabScroll.VerticalOffset, tabScroll.ScrollableHeight, upward, out var next))
            return;

        tabScroll.ChangeView(null, next, null, disableAnimation: true);
        e.Handled = true;
    }

    private static bool IsChord()
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        return ctrl.HasFlag(CoreVirtualKeyStates.Down) || alt.HasFlag(CoreVirtualKeyStates.Down);
    }

    private static bool KeepsVerticalArrows(DependencyObject? node)
    {
        for (var current = node; current != null; current = VisualTreeHelper.GetParent(current))
        {
            switch (current)
            {
                case TextBox text when text.AcceptsReturn:
                case RichEditBox:
                case PasswordBox:
                case AutoSuggestBox:
                case NumberBox:
                case ComboBox:
                case ComboBoxItem:
                case Slider:
                case RadioButtons:
                case RadioButton:
                case CalendarDatePicker:
                case DatePicker:
                case TimePicker:
                    return true;
            }
        }

        return false;
    }
}
