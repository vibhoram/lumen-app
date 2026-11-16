// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIsland;

/// <summary>
/// Proportional wheel scrolling for a ScrollViewer. WPF scrolls a fixed 3 lines (48 px) per wheel
/// message whatever its delta, so a precision touchpad, which sends many small-delta messages per
/// swipe, scrolls far too fast. This scrolls by the delta instead: one mouse notch (120) still
/// moves <c>pixelsPerNotch</c>, and touchpad swipes move in proportion.
/// Optional <c>smooth</c> eases the offset so Settings doesn't feel like hard steps.
/// </summary>
public static class WheelScrolling
{
    public static void Attach(ScrollViewer scroller, double pixelsPerNotch = 48, bool smooth = false)
    {
        if (!smooth)
        {
            scroller.PreviewMouseWheel += (_, e) =>
            {
                if (scroller.ScrollableHeight <= 0) return;
                var target = scroller.VerticalOffset - (double)e.Delta / Mouse.MouseWheelDeltaForOneLine * pixelsPerNotch;
                target = Math.Clamp(target, 0, scroller.ScrollableHeight);
                if (Math.Abs(target - scroller.VerticalOffset) < 0.01) return;
                scroller.ScrollToVerticalOffset(target);
                e.Handled = true;
            };
            return;
        }

        var targetOffset = scroller.VerticalOffset;
        var ticking = false;

        void StopTick()
        {
            if (!ticking) return;
            CompositionTarget.Rendering -= Tick;
            ticking = false;
        }

        void Tick(object? _, EventArgs __)
        {
            if (!scroller.IsLoaded || scroller.ScrollableHeight <= 0)
            {
                targetOffset = scroller.VerticalOffset;
                StopTick();
                return;
            }

            targetOffset = Math.Clamp(targetOffset, 0, scroller.ScrollableHeight);
            var cur = scroller.VerticalOffset;
            var next = cur + (targetOffset - cur) * 0.28;
            if (Math.Abs(targetOffset - next) < 0.35)
            {
                scroller.ScrollToVerticalOffset(targetOffset);
                StopTick();
                return;
            }
            scroller.ScrollToVerticalOffset(next);
        }

        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (scroller.ScrollableHeight <= 0) return;
            targetOffset = Math.Clamp(
                targetOffset - (double)e.Delta / Mouse.MouseWheelDeltaForOneLine * pixelsPerNotch,
                0, scroller.ScrollableHeight);
            if (!ticking)
            {
                ticking = true;
                CompositionTarget.Rendering += Tick;
            }
            e.Handled = true;
        };

        scroller.ScrollChanged += (_, e) =>
        {
            // Content rebuild (Settings presets) shrinks extent — clamp so we never chase a dead target.
            if (e.ExtentHeightChange != 0)
            {
                targetOffset = Math.Clamp(scroller.VerticalOffset, 0, Math.Max(0, scroller.ScrollableHeight));
                if (scroller.ScrollableHeight <= 0) StopTick();
                return;
            }
            if (!ticking) targetOffset = scroller.VerticalOffset;
        };

        scroller.Unloaded += (_, _) => StopTick();
    }
}
