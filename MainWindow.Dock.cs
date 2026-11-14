// D-Island (personal fork) - dock-style page tabs: icons swell under the cursor like the macOS Dock, neighbours
// make room, and resting on an icon for a moment opens its page, so sweeping across the strip flips through pages.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIsland;

public partial class MainWindow
{
    private const double DockSigma = 30;      // how far the swell reaches (px)
    private const double DockAmplitude = 0.62; // extra scale under the cursor (1 -> 1.62)
    private const double HoverSwitchMs = 120;

    private double _dockX = double.NaN;        // cursor x inside the tab strip; NaN = not hovering
    private double _lastDockX;
    private bool _dockRunning;
    private DateTime _dockLastFrame;
    private ExpandedPage? _hoverPage;
    private DateTime _hoverSince;
    private readonly Dictionary<Button, double> _dockScale = new();
    private DateTime _dockArmedAt = DateTime.MinValue; // hover-switching stays off until this moment
    private double _dockAnchorX = double.NaN;          // where the cursor was when hover-switching became possible
    private bool _dockMoved;                           // the cursor really travelled along the strip since then

    /// <summary>When the island opens under a resting cursor it must not jump to whichever page is below it.</summary>
    private void ArmDock()
    {
        _dockArmedAt = DateTime.UtcNow.AddMilliseconds(650);
        _dockAnchorX = double.NaN;
        _dockMoved = false;
        _hoverPage = null;
    }

    private void WireDock()
    {
        PageTabs.MouseMove += (_, e) =>
        {
            _dockX = e.GetPosition(PageTabs).X;
            _lastDockX = _dockX;
            if (DateTime.UtcNow >= _dockArmedAt)
            {
                if (double.IsNaN(_dockAnchorX)) _dockAnchorX = _dockX;
                else if (Math.Abs(_dockX - _dockAnchorX) > 16) _dockMoved = true;
            }
            StartDock();
        };
        PageTabs.MouseLeave += (_, _) =>
        {
            _dockX = double.NaN;
            _hoverPage = null;
            _dockAnchorX = double.NaN;
            _dockMoved = false;
            StartDock(); // relax back to normal size
        };
    }

    private void StartDock()
    {
        if (_dockRunning) return;
        _dockRunning = true;
        _dockLastFrame = DateTime.UtcNow;
        CompositionTarget.Rendering += DockFrame;
    }

    private void StopDock()
    {
        CompositionTarget.Rendering -= DockFrame;
        _dockRunning = false;
    }

    private void DockFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _dockLastFrame).TotalSeconds, 0.001, 0.05);
        _dockLastFrame = now;
        var alpha = 1 - Math.Exp(-dt * 20); // smooth, frame-rate independent easing toward the target

        var hovering = !double.IsNaN(_dockX) && _settings.DockMagnify && _state.IsExpanded;
        var count = _tabs.Count;
        if (count == 0) { StopDock(); return; }

        var left = new double[count];
        var width = new double[count];
        var scale = new double[count];
        var settled = true;
        for (var i = 0; i < count; i++)
        {
            var b = _tabs[i].Button;
            left[i] = b.TranslatePoint(new Point(0, 0), PageTabs).X;
            width[i] = b.ActualWidth;
            var center = left[i] + width[i] / 2;
            var target = hovering ? 1 + DockAmplitude * Math.Exp(-Math.Pow((_dockX - center) / DockSigma, 2)) : 1;
            _dockScale.TryGetValue(b, out var current);
            if (current == 0) current = 1;
            current += (target - current) * alpha;
            if (Math.Abs(target - current) > 0.002) settled = false;
            else current = target;
            scale[i] = current;
            _dockScale[b] = current;
        }

        // Neighbours make room: each icon moves by the extra width of everything before it (plus half its own).
        var offset = new double[count];
        var running = 0.0;
        for (var i = 0; i < count; i++)
        {
            var extra = width[i] * (scale[i] - 1);
            offset[i] = running + extra / 2;
            running += extra;
        }
        // Keep the point under the cursor still: the strip grows outward from it, like the Dock.
        var anchor = 0.0;
        var x = hovering ? _dockX : _lastDockX;
        if (count == 1) anchor = offset[0];
        else
        {
            anchor = offset[0];
            for (var i = 0; i < count - 1; i++)
            {
                var c0 = left[i] + width[i] / 2;
                var c1 = left[i + 1] + width[i + 1] / 2;
                if (x >= c0 && x <= c1) { anchor = offset[i] + (offset[i + 1] - offset[i]) * ((x - c0) / Math.Max(1, c1 - c0)); break; }
                if (x > c1) anchor = offset[i + 1];
            }
        }

        for (var i = 0; i < count; i++)
        {
            var group = (TransformGroup)_tabs[i].Button.RenderTransform;
            var s = (ScaleTransform)group.Children[0];
            var tr = (TranslateTransform)group.Children[1];
            s.ScaleX = s.ScaleY = scale[i];
            System.Windows.Controls.Panel.SetZIndex(_tabs[i].Button, (int)(scale[i] * 100));
            tr.X = offset[i] - anchor;
            tr.Y = -(scale[i] - 1) * 7; // rises a little, like a Dock icon
        }

        // Rest on an icon for a moment and its page opens.
        if (hovering && _settings.HoverSwitchPages && _dockMoved && now >= _dockArmedAt)
        {
            ExpandedPage? over = null;
            for (var i = 0; i < count; i++)
                if (_dockX >= left[i] - 2 && _dockX <= left[i] + width[i] + 2) { over = _tabs[i].Page; break; }
            if (over != _hoverPage) { _hoverPage = over; _hoverSince = now; }
            else if (over is { } page && page != _page && (now - _hoverSince).TotalMilliseconds >= HoverSwitchMs)
                ShowPage(page, animate: true);
        }

        if (settled && !hovering) StopDock();
    }
}
