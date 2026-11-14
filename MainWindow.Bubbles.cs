// D-Island (personal fork) - progress rings on the activity bubbles beside the pill.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private static readonly Brush RingTimer = FrozenBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
    private static readonly Brush RingFocus = FrozenBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush RingMedia = FrozenBrush(Color.FromArgb(0xE6, 255, 255, 255));
    private static readonly Brush RingStopwatch = FrozenBrush(Color.FromRgb(0x64, 0xD2, 0xFF));

    /// <summary>A timer bubble shows how much time is left; a music bubble how far into the song; a stopwatch spins once a minute.</summary>
    private void UpdateBubbleRings()
    {
        if (_state is null) return;
        var allowed = _settings.ShowSecondActivity && !_state.IsExpanded && _state.Transient is null;
        SetRing(ActivityBubble, ActivityBubbleRing, allowed ? _state.SecondaryView : null);
        SetRing(ActivityBubble2, ActivityBubbleRing2, allowed ? _state.TertiaryView : null);
        if (MediaDiscBubble.Visibility == Visibility.Visible && _media.Current is { Duration.Ticks: > 0 } media)
            MediaDiscRing.Data = RingGeometry(Math.Max(26, MediaDiscBubble.Width), Math.Clamp(media.EstimatePosition(DateTimeOffset.Now) / media.Duration, 0, 1));
    }

    private void SetRing(Grid bubble, Path ring, IslandView? view)
    {
        if (view is null || bubble.Visibility != Visibility.Visible) { ring.Data = Geometry.Empty; return; }
        double fraction;
        Brush brush;
        switch (view)
        {
            case IslandView.Timer:
                fraction = _timer.Total.TotalSeconds > 0 ? _timer.Remaining.TotalSeconds / _timer.Total.TotalSeconds : 0;
                brush = _focusSession.Phase != FocusPhase.Idle ? RingFocus : RingTimer;
                break;
            case IslandView.Media:
                var media = _media.Current;
                fraction = media is { Duration.Ticks: > 0 } ? media.EstimatePosition(DateTimeOffset.Now) / media.Duration : 0;
                brush = RingMedia;
                break;
            default:
                fraction = _stopwatch.Elapsed.TotalSeconds % 60 / 60;
                brush = RingStopwatch;
                break;
        }
        ring.Stroke = brush;
        ring.Data = RingGeometry(Math.Max(26, bubble.ActualWidth > 0 ? bubble.ActualWidth : bubble.Width), Math.Clamp(fraction, 0, 1));
    }

    private static Geometry RingGeometry(double size, double fraction)
    {
        var radius = size / 2 - 1.6;
        var center = new Point(size / 2, size / 2);
        if (fraction >= 0.995) return new EllipseGeometry(center, radius, radius);
        if (fraction <= 0.004) return Geometry.Empty;
        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }
}
