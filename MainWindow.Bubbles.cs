// D-Island (personal fork) - side activity chrome: media/bubble rings + the liquid-glass activity strand.

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private static readonly Brush RingTimer = FrozenBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
    private static readonly Brush RingFocus = FrozenBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush RingMedia = FrozenBrush(Color.FromArgb(0xE6, 255, 255, 255));
    private static readonly Brush RingStopwatch = FrozenBrush(Color.FromRgb(0x64, 0xD2, 0xFF));
    private static readonly Color AccentAlarm = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color AccentTimer = Color.FromRgb(0xBF, 0x5A, 0xF2);
    private static readonly Color AccentFocus = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color AccentStopwatch = Color.FromRgb(0x64, 0xD2, 0xFF);

    private IReadOnlyList<IslandView> _strandViews = Array.Empty<IslandView>();
    private bool _strandWideHours; // remasure only when crossing into/out of H:MM:SS

    /// <summary>Media/bubble orb rings + live times / spine on the activity strand.</summary>
    private void UpdateBubbleRings()
    {
        if (_state is null) return;
        var allowed = _settings.ShowSecondActivity && !_state.IsExpanded && _state.Transient is null;
        SetRing(ActivityBubble, ActivityBubbleRing, ActivityBubbleGlyph,
            allowed && ActivityBubble.Tag is IslandView a ? a : null);
        SetRing(ActivityBubble2, ActivityBubbleRing2, ActivityBubbleGlyph2,
            allowed && ActivityBubble2.Tag is IslandView b ? b : null);

        if (MediaDiscBubble.Visibility == Visibility.Visible && _media.Current is { Duration.Ticks: > 0 } media)
        {
            var disc = Math.Max(26, Math.Min(
                MediaDiscBubble.ActualWidth > 0 ? MediaDiscBubble.ActualWidth : MediaDiscBubble.Width,
                MediaView.Height > 0 ? MediaView.Height : 40));
            MediaDiscRing.Data = RingGeometry(disc, Math.Clamp(media.EstimatePosition(DateTimeOffset.Now) / media.Duration, 0, 1));
        }

        RefreshActivityStrandTimes();
    }

    private void SetRing(Grid bubble, Path ring, TextBlock glyph, IslandView? view)
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
            case IslandView.AlarmSet:
                fraction = _nextAlarmAt is { } due ? Math.Clamp(1 - (due - DateTime.Now).TotalHours / 12, 0, 1) : 0;
                brush = RingTimer;
                break;
            default:
                fraction = _stopwatch.Elapsed.TotalSeconds % 60 / 60;
                brush = RingStopwatch;
                break;
        }
        if (view == IslandView.Timer)
        {
            glyph.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            glyph.FontSize = 10.5;
            glyph.FontWeight = FontWeights.SemiBold;
            glyph.Text = TimerService.Format(_timer.Remaining);
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

    /// <summary>
    /// One liquid-glass capsule holding every clock-side activity. Glyph + live time per slot,
    /// linked by a spectrum spine — equal end padding so nothing clips in the rounded caps.
    /// </summary>
    private bool BuildActivityStrand(IReadOnlyList<IslandView> items)
    {
        var show = items.Count > 0;
        var was = ActivityStrand.Visibility == Visibility.Visible;
        ActivityStrandRow.Children.Clear();
        _strandViews = show ? items : Array.Empty<IslandView>();
        if (!show)
        {
            ActivityStrand.Visibility = Visibility.Collapsed;
            ActivityStrand.Width = double.NaN;
            return was;
        }

        // Three slots: tighter type so the capsule stays readable without clipping.
        var tight = items.Count >= 3;
        var iconSize = tight ? 10.0 : 11.0;
        var timeSize = tight ? 10.5 : 11.5;
        var iconGap = tight ? 3.0 : 5.0;
        var beadGap = tight ? 4.0 : 6.0;

        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                ActivityStrandRow.Children.Add(new Ellipse
                {
                    Width = 2.5, Height = 2.5, Margin = new Thickness(beadGap, 0, beadGap, 0),
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                });
            }

            var view = items[i];
            var accent = StrandAccent(view);
            var cell = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = view,
                Background = Brushes.Transparent,
            };
            cell.MouseLeftButtonUp += OnActivityStrandCellClick;

            cell.Children.Add(new TextBlock
            {
                Text = StrandGlyph(view),
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = iconSize,
                Foreground = FrozenBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, iconGap, 0),
                IsHitTestVisible = false,
            });
            var time = new TextBlock
            {
                Text = StrandTime(view),
                FontSize = timeSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = FrozenBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                Tag = "time",
            };
            // Tabular figures + fixed slot width so "1"→"2" doesn't resize the capsule every tick.
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            time.Width = StrandTimeSlotWidth(view, timeSize);
            cell.Children.Add(time);
            cell.ToolTip = StrandTip(view);
            ActivityStrandRow.Children.Add(cell);
        }

        PaintStrandSpine(items);
        ActivityStrand.Visibility = Visibility.Visible;
        _strandWideHours = StrandNeedsHourWidth();
        RemeasureActivityStrand(ActivityStrand.Height > 0 ? ActivityStrand.Height : 34);

        if (!was)
        {
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(320 / SpeedFactor))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
            };
            Timeline.SetDesiredFrameRate(pop, FrameRateCap);
            var scale = (ScaleTransform)ActivityStrand.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        return true;
    }

    /// <summary>
    /// Size the capsule once from fixed time slots (not live digit widths) so ticks don't jitter.
    /// </summary>
    private void RemeasureActivityStrand(double strandH)
    {
        if (ActivityStrand.Visibility != Visibility.Visible) return;
        var pad = Math.Max(11, strandH * 0.4);
        // Center the row so any leftover width splits left/right (Left + fudge looked biased).
        ActivityStrandRow.Margin = new Thickness(pad, 0, pad, 0);
        ActivityStrandRow.HorizontalAlignment = HorizontalAlignment.Center;
        ActivityStrandRow.VerticalAlignment = VerticalAlignment.Center;
        ActivityStrandSpine.Margin = new Thickness(pad, 0, pad, Math.Max(4, strandH * 0.12));

        double content = 0;
        foreach (UIElement child in ActivityStrandRow.Children)
        {
            if (child is not FrameworkElement fe) continue;
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            content += fe.DesiredSize.Width + fe.Margin.Left + fe.Margin.Right;
        }
        ActivityStrand.Width = Math.Max(56, Math.Ceiling(content + pad * 2));
    }

    /// <summary>Widest stable template for this slot — digits tick inside it, width stays put.</summary>
    private double StrandTimeSlotWidth(IslandView view, double fontSize)
    {
        var sample = view switch
        {
            IslandView.AlarmSet => "00:00",
            IslandView.Timer or IslandView.Stopwatch when StrandNeedsHourWidth() => "0:00:00",
            IslandView.Timer or IslandView.Stopwatch => "00:00",
            _ => "00:00",
        };
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(sample, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize, Brushes.White, dpi);
        return Math.Ceiling(ft.Width) + 1;
    }

    private bool StrandNeedsHourWidth() =>
        _strandViews.Any(v => v == IslandView.Timer && _timer.Remaining.TotalHours >= 1
                           || v == IslandView.Stopwatch && _stopwatch.Elapsed.TotalHours >= 1);

    private void RefreshActivityStrandTimes()
    {
        if (ActivityStrand.Visibility != Visibility.Visible) return;
        foreach (var child in ActivityStrandRow.Children)
        {
            if (child is not StackPanel { Tag: IslandView view } cell) continue;
            foreach (var part in cell.Children)
            {
                if (part is TextBlock { Tag: "time" } time)
                {
                    time.Text = StrandTime(view);
                    time.Foreground = FrozenBrush(StrandAccent(view));
                }
            }
            cell.ToolTip = StrandTip(view);
        }
        PaintStrandSpine(_strandViews);

        // Only remasure when the format gains/loses an hour field — never on normal second ticks.
        var wide = StrandNeedsHourWidth();
        if (wide == _strandWideHours) return;
        _strandWideHours = wide;
        var tight = _strandViews.Count >= 3;
        var timeSize = tight ? 10.5 : 11.5;
        foreach (var child in ActivityStrandRow.Children)
        {
            if (child is not StackPanel { Tag: IslandView view } cell) continue;
            foreach (var part in cell.Children)
            {
                if (part is TextBlock { Tag: "time" } time)
                    time.Width = StrandTimeSlotWidth(view, timeSize);
            }
        }
        var h = ActivityStrand.Height > 0 ? ActivityStrand.Height : 34;
        RemeasureActivityStrand(h);
        ApplyGeometry();
    }

    private void PaintStrandSpine(IReadOnlyList<IslandView> items)
    {
        if (items.Count == 0)
        {
            ActivityStrandSpine.Background = null;
            return;
        }
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (items.Count == 1)
        {
            var c = StrandAccent(items[0]); c.A = 0xCC;
            var fade = c; fade.A = 0;
            brush.GradientStops.Add(new GradientStop(fade, 0));
            brush.GradientStops.Add(new GradientStop(c, 0.5));
            brush.GradientStops.Add(new GradientStop(fade, 1));
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var c = StrandAccent(items[i]);
                c.A = 0xCC;
                var t = 0.1 + (double)i / (items.Count - 1) * 0.8;
                brush.GradientStops.Add(new GradientStop(c, t));
            }
            var a = StrandAccent(items[0]); a.A = 0;
            var b = StrandAccent(items[^1]); b.A = 0;
            brush.GradientStops.Insert(0, new GradientStop(a, 0));
            brush.GradientStops.Add(new GradientStop(b, 1));
        }
        brush.Freeze();
        ActivityStrandSpine.Background = brush;
    }

    private Color StrandAccent(IslandView view) => view switch
    {
        IslandView.AlarmSet => AccentAlarm,
        IslandView.Timer => _focusSession.Phase != FocusPhase.Idle ? AccentFocus : AccentTimer,
        IslandView.Stopwatch => AccentStopwatch,
        _ => Color.FromRgb(0x99, 0x99, 0xA0),
    };

    private static string StrandGlyph(IslandView view) => view switch
    {
        IslandView.AlarmSet => "\uE823",
        IslandView.Timer => "\uE916",
        IslandView.Stopwatch => "\uE916",
        _ => "\uE916",
    };

    private string StrandTime(IslandView view) => view switch
    {
        IslandView.AlarmSet => _nextAlarmAt is { } at ? at.ToString("h:mm") : "—",
        IslandView.Timer => TimerService.Format(_timer.Remaining),
        IslandView.Stopwatch => StopwatchService.Format(_stopwatch.Elapsed, tenths: false),
        _ => "—",
    };

    private string StrandTip(IslandView view) => view switch
    {
        IslandView.AlarmSet => _nextAlarmAt is { } when ? "Alarm · " + when.ToString("h:mm tt") : "Alarm",
        IslandView.Timer when _focusSession.Phase != FocusPhase.Idle => "Focus · " + TimerService.Format(_timer.Remaining),
        IslandView.Timer => "Timer · " + TimerService.Format(_timer.Remaining),
        IslandView.Stopwatch => "Stopwatch · " + StopwatchService.Format(_stopwatch.Elapsed, tenths: false),
        _ => "",
    };

    private void OnActivityStrandCellClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IslandView view }) return;
        e.Handled = true;
        if (IslandHidden) SetIslandHidden(false);
        SetExpanded(true);
        var mode = view switch
        {
            IslandView.AlarmSet => ClockMode.Alarm,
            IslandView.Timer => ClockMode.Timer,
            IslandView.Stopwatch => ClockMode.Stopwatch,
            _ => ClockMode.None,
        };
        if (mode != ClockMode.None) SetClockMode(mode);
    }
}
