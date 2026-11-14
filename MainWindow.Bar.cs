// Lumen (personal fork) - "Thin bar" mode: at rest the island is a hairline of glass near the top centre of the screen.
// Hover it and it springs open into the normal island; after you leave, it stays a pill for a chosen number of seconds
// and then folds back into the line. Anything running (music, a timer, a notification, an alarm) shows as usual.

using System.Windows;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private const double BarWidth = 110;
    private const double BarHeight = 6;

    private DateTime _barRevealUntil = DateTime.MinValue;
    private bool _barWasIdle;
    private readonly DispatcherTimer _barTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>True while Bar mode is at rest: collapsed, nothing to show, and the reveal time has run out.</summary>
    private bool BarIsIdle =>
        _settings is not null && _settings.Mode == IslandMode.Bar && _state is not null && !_state.IsExpanded
        && _state.Transient is null && _state.PersistentView == IslandView.Clock && !IslandHidden
        && DateTime.UtcNow >= _barRevealUntil;

    private void WireBar()
    {
        _barTimer.Tick += (_, _) => BarTick();
        _barTimer.Start();
    }

    private void BarTick()
    {
        if (_settings.Mode != IslandMode.Bar)
        {
            if (_barWasIdle) { _barWasIdle = false; Render(); }
            return;
        }
        var now = DateTime.UtcNow;
        var seconds = Math.Clamp(_settings.BarRevealSeconds, 1, 60);

        // being open (or anything on show) keeps the pill out; the clock starts when it folds back
        if (_state.IsExpanded || _state.Transient is not null || _state.PersistentView != IslandView.Clock)
            _barRevealUntil = now.AddSeconds(seconds);
        else if (CursorNearBar())
            _barRevealUntil = now.AddSeconds(seconds);

#if DEBUG
        if (Environment.GetEnvironmentVariable("DISLAND_BAR_LOG") is { Length: > 0 } log)
        {
            NativeMethods.GetCursorPos(out var c);
            var q = Root.PointFromScreen(new Point(c.X, c.Y));
            System.IO.File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} cursor=({c.X},{c.Y}) root=({q.X:0},{q.Y:0}) near={CursorNearBar()} idle={BarIsIdle} until={_barRevealUntil:HH:mm:ss} expanded={_state.IsExpanded} w={_width.Target:0} h={_height.Target:0} view={_state.CurrentView} vw={ClockView.Width:0} cur={_width.Value:0}x{_height.Value:0} op={ClockView.Opacity} anim={_animating}" + Environment.NewLine);
        }
#endif
        var idle = BarIsIdle;
        if (idle != _barWasIdle)
        {
            _barWasIdle = idle;
            Render();               // springs between the line and the pill
            UpdateSecondActivity();
            UpdateIdleWeatherBubble();
            ApplyGeometry();
        }
    }

    /// <summary>The hairline is too thin to hover reliably: the zone around it (a bit wider and taller) counts.</summary>
    private bool CursorNearBar()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return false;
        var p = Root.PointFromScreen(new Point(pt.X, pt.Y)); // design units
        var w = BarIsIdle ? BarWidth : Math.Max(BarWidth, _width.Target);
        var x = (BaseWidth - w) / 2;
        return p.X >= x - 30 && p.X <= x + w + 30 && p.Y >= -400 && p.Y <= PillTop + (BarIsIdle ? BarHeight : _height.Target) + 18;
    }
}
