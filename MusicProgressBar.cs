// D-Island (personal fork) - the music progress bar in six looks, drawn directly (no per-style XAML).

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DIsland;

public enum ProgressStyle { Line, Pill, Wavy, Soundwave, Dots, Glow, Ribbon, Aurora, Pulse, Beads, Spark }

public sealed class MusicProgressBar : FrameworkElement
{
    private readonly DispatcherTimer _animate = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;
    private double _fraction;
    private ProgressStyle _style = ProgressStyle.Line;
    private bool _playing;
    private Color _accent = Colors.White;
    private float[] _bars = Array.Empty<float>();
    private string _seed = "";

    public MusicProgressBar()
    {
        Height = 22;
        SnapsToDevicePixels = false;
        _animate.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => UpdateTimer();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    public double Fraction
    {
        get => _fraction;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - _fraction) < 0.0004) return;
            _fraction = value;
            InvalidateVisual();
        }
    }

    public new ProgressStyle Style
    {
        get => _style;
        set { _style = value; UpdateTimer(); InvalidateVisual(); }
    }

    public bool Playing
    {
        get => _playing;
        set { _playing = value; UpdateTimer(); InvalidateVisual(); }
    }

    private IReadOnlyList<Color> _palette = Array.Empty<Color>();

    /// <summary>The cover's colours: the played part is painted with them, left to right, like the wave on the collapsed pill.</summary>
    public IReadOnlyList<Color> Palette
    {
        get => _palette;
        set { _palette = value ?? Array.Empty<Color>(); InvalidateVisual(); }
    }

    private Color ColorAt(double f)
    {
        if (_palette.Count == 0) return _accent;
        if (_palette.Count == 1) return _palette[0];
        f = Math.Clamp(f, 0, 1) * (_palette.Count - 1);
        var i = Math.Min((int)f, _palette.Count - 2);
        return DIsland.Services.ColorTools.Mix(_palette[i], _palette[i + 1], f - i);
    }

    private Brush PlayedBrush(double width)
    {
        if (_palette.Count < 2) return new SolidColorBrush(_accent);
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Max(1, width), 0) };
        for (var i = 0; i < _palette.Count; i++) brush.GradientStops.Add(new GradientStop(_palette[i], i / (double)(_palette.Count - 1)));
        return brush;
    }

    public Color Accent
    {
        get => _accent;
        set { _accent = value; InvalidateVisual(); }
    }

    /// <summary>Makes the soundwave bars look different (and stay the same) for each song.</summary>
    public string Seed
    {
        get => _seed;
        set { _seed = value; _bars = Array.Empty<float>(); InvalidateVisual(); }
    }

    // Only the wavy look and the soundwave "breathing" animate, and only while playing and on screen.
    private void UpdateTimer()
    {
        var wanted = IsVisible && _playing && _style is ProgressStyle.Wavy or ProgressStyle.Soundwave or ProgressStyle.Glow or ProgressStyle.Ribbon or ProgressStyle.Aurora or ProgressStyle.Pulse or ProgressStyle.Beads or ProgressStyle.Spark;
        if (wanted && !_animate.IsEnabled) _animate.Start();
        else if (!wanted && _animate.IsEnabled) _animate.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 8 || h < 4) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // so the whole band is hit-testable

        var cy = h / 2;
        var x = w * _fraction;
        var played = PlayedBrush(w);
        var track = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        var t = (DateTime.UtcNow - _start).TotalSeconds;

        switch (_style)
        {
            case ProgressStyle.Line:
            {
                dc.DrawRoundedRectangle(track, null, new Rect(0, cy - 2, w, 4), 2, 2);
                if (x > 0) dc.DrawRoundedRectangle(played, null, new Rect(0, cy - 2, x, 4), 2, 2);
                if (IsMouseOver) dc.DrawEllipse(played, null, new Point(Math.Clamp(x, 5, w - 5), cy), 5, 5);
                break;
            }
            case ProgressStyle.Pill:
            {
                dc.DrawRoundedRectangle(track, null, new Rect(0, cy - 5, w, 10), 5, 5);
                if (x > 1)
                {
                    var fill = new LinearGradientBrush(Color.FromArgb(0xFF, _accent.R, _accent.G, _accent.B), Color.FromArgb(0xB0, 255, 255, 255), 0);
                    dc.DrawRoundedRectangle(fill, null, new Rect(0, cy - 5, Math.Max(10, x), 10), 5, 5);
                    // a thin highlight along the top of the fill, like a glass rod
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), null, new Rect(3, cy - 4, Math.Max(4, x - 6), 2.2), 1.1, 1.1);
                }
                break;
            }
            case ProgressStyle.Wavy:
            {
                var phase = _playing ? t * 5.5 : 0;
                // the part still to play: a calm straight line
                dc.DrawLine(new Pen(track, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(Math.Min(x, w), cy), new Point(w, cy));
                if (x > 1)
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        const double wavelength = 18, amp = 3.6;
                        ctx.BeginFigure(new Point(0, cy), false, false);
                        for (var px = 1.0; px <= x; px += 1.5)
                        {
                            // the wave eases in from the left so it doesn't start abruptly
                            var ease = Math.Min(1, px / 14);
                            ctx.LineTo(new Point(px, cy + Math.Sin(px / wavelength * 2 * Math.PI - phase) * amp * ease), true, true);
                        }
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, new Pen(played, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geo);
                }
                dc.DrawRoundedRectangle(played, null, new Rect(Math.Clamp(x, 1.5, w - 1.5) - 1.5, cy - 8, 3, 16), 1.5, 1.5);
                break;
            }
            case ProgressStyle.Soundwave:
            {
                const double barW = 3, gap = 2.2;
                var count = Math.Max(8, (int)(w / (barW + gap)));
                EnsureBars(count);
                for (var i = 0; i < count; i++)
                {
                    var bx = i * (barW + gap);
                    var frac = (bx + barW / 2) / w;
                    var breathe = _playing ? 1 + 0.18 * Math.Sin(t * 6 + i * 0.7) : 1;
                    var near = Math.Exp(-Math.Pow((frac - _fraction) * 14, 2)); // bars at the playhead swell
                    var bh = Math.Clamp((3 + _bars[i] * (h - 7)) * breathe * (1 + 0.35 * near), 3, h - 1);
                    var brush = frac <= _fraction ? new SolidColorBrush(ColorAt(frac)) : track;
                    dc.DrawRoundedRectangle(brush, null, new Rect(bx, cy - bh / 2, barW, bh), 1.5, 1.5);
                }
                break;
            }
            case ProgressStyle.Dots:
            {
                const double step = 7;
                var count = Math.Max(6, (int)(w / step));
                for (var i = 0; i < count; i++)
                {
                    var dx = (i + 0.5) * (w / count);
                    var isPlayed = dx <= x;
                    var radius = isPlayed ? 2.5 : 1.8;
                    dc.DrawEllipse(isPlayed ? new SolidColorBrush(ColorAt(dx / w)) : track, null, new Point(dx, cy), radius, radius);
                }
                dc.DrawEllipse(played, null, new Point(Math.Clamp(x, 4, w - 4), cy), 5, 5);
                break;
            }
            case ProgressStyle.Ribbon:
            {
                dc.DrawLine(new Pen(track, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(Math.Min(x, w), cy), new Point(w, cy));
                if (x > 2)
                {
                    for (var layer = 0; layer < 2; layer++)
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(0, h), true, true);
                            for (var px = 0.0; px <= x; px += 2)
                            {
                                var ease = Math.Min(1, px / 16);
                                var wave = Math.Sin(px / (layer == 0 ? 26 : 38) * 2 * Math.PI - t * (layer == 0 ? 4 : -3) + layer * 2) * (layer == 0 ? 3.2 : 2.2) * ease;
                                ctx.LineTo(new Point(px, cy + wave - (layer == 0 ? 1 : 0)), true, false);
                            }
                            ctx.LineTo(new Point(x, h), true, false);
                        }
                        geo.Freeze();
                        var fill = PlayedBrush(w);
                        fill.Opacity = layer == 0 ? 0.55 : 0.28;
                        dc.DrawGeometry(fill, null, geo);
                    }
                    dc.DrawLine(new Pen(played, 2) { StartLineCap = PenLineCap.Round }, new Point(0, cy), new Point(x, cy - 1));
                }
                dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, 4, w - 4), cy - 1), 3.6, 3.6);
                break;
            }
            case ProgressStyle.Aurora:
            {
                dc.DrawLine(new Pen(track, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(Math.Min(x, w), cy), new Point(w, cy));
                if (x > 2)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(0, cy), false, false);
                            for (var px = 1.0; px <= x; px += 2)
                            {
                                var ease = Math.Min(1, px / 18);
                                ctx.LineTo(new Point(px, cy + Math.Sin(px / (20 + k * 9) * 2 * Math.PI - t * (3.2 + k) * (k == 1 ? -1 : 1) + k * 1.7) * (3.4 - k * 0.7) * ease), true, true);
                            }
                        }
                        geo.Freeze();
                        var col = ColorAt(k / 2.0);
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(235 - k * 55), col.R, col.G, col.B)), 2.4 - k * 0.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawGeometry(null, pen, geo);
                    }
                }
                var headColor = ColorAt(1);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x40, headColor.R, headColor.G, headColor.B)), null, new Point(Math.Clamp(x, 6, w - 6), cy), 8, 8);
                dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, 4, w - 4), cy), 3.4, 3.4);
                break;
            }
            case ProgressStyle.Pulse:
            {
                dc.DrawLine(new Pen(track, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(Math.Min(x, w), cy), new Point(w, cy));
                if (x > 2)
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(new Point(0, cy), false, false);
                        for (var px = 1.0; px <= x; px += 1)
                        {
                            var u = ((px / 90.0) - (_playing ? t * 0.35 : 0)) % 1.0;
                            if (u < 0) u += 1;
                            var spike = u < 0.08 ? -Math.Sin(u / 0.08 * Math.PI) * 8 : u < 0.14 ? Math.Sin((u - 0.08) / 0.06 * Math.PI) * 3.5 : 0;
                            ctx.LineTo(new Point(px, cy + spike), true, true);
                        }
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, new Pen(played, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
                }
                dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, 4, w - 4), cy), 3.6, 3.6);
                break;
            }
            case ProgressStyle.Beads:
            {
                const double step = 9;
                var count = Math.Max(6, (int)(w / step));
                for (var i = 0; i < count; i++)
                {
                    var dx = (i + 0.5) * (w / count);
                    var isPlayed = dx <= x;
                    var wave = Math.Sin(dx / 34 * 2 * Math.PI - (_playing ? t * 3 : 0));
                    var radius = (isPlayed ? 2.3 : 1.6) + (isPlayed ? 1.2 * (wave * 0.5 + 0.5) : 0);
                    dc.DrawEllipse(isPlayed ? new SolidColorBrush(ColorAt(dx / w)) : track, null, new Point(dx, cy + wave * (isPlayed ? 3.2 : 0)), radius, radius);
                }
                dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, 4, w - 4), cy), 4, 4);
                break;
            }
            case ProgressStyle.Spark:
            {
                dc.DrawRoundedRectangle(track, null, new Rect(0, cy - 1, w, 2), 1, 1);
                if (x > 1) dc.DrawRoundedRectangle(played, null, new Rect(0, cy - 1.4, x, 2.8), 1.4, 1.4);
                var rng = new Random(_seed.GetHashCode());
                for (var i = 0; i < 16; i++)
                {
                    var sx = rng.NextDouble() * Math.Max(1, x);
                    var phase = rng.NextDouble() * 6.28;
                    var twinkle = _playing ? 0.5 + 0.5 * Math.Sin(t * (2 + rng.NextDouble() * 3) + phase) : 0.5;
                    var sy = cy + (rng.NextDouble() - 0.5) * (h - 6);
                    var c = ColorAt(sx / w);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(40 + 200 * twinkle), c.R, c.G, c.B)), null, new Point(sx, sy), 0.8 + 1.4 * twinkle, 0.8 + 1.4 * twinkle);
                }
                var hc = ColorAt(1);
                var head = new Point(Math.Clamp(x, 5, w - 5), cy);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, hc.R, hc.G, hc.B)), null, head, 8, 8);
                dc.DrawEllipse(Brushes.White, null, head, 3.6, 3.6);
                break;
            }
            case ProgressStyle.Glow:
            {
                var glowColor = Color.FromArgb(0x38, _accent.R, _accent.G, _accent.B);
                dc.DrawRoundedRectangle(track, null, new Rect(0, cy - 1.5, w, 3), 1.5, 1.5);
                if (x > 1)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(glowColor), null, new Rect(0, cy - 5, x, 10), 5, 5); // soft halo
                    var line = new LinearGradientBrush(Color.FromArgb(0x30, _accent.R, _accent.G, _accent.B), _accent, 0);
                    dc.DrawRoundedRectangle(line, null, new Rect(0, cy - 1.8, x, 3.6), 1.8, 1.8);
                }
                var pulse = _playing ? 1 + 0.15 * Math.Sin(t * 4) : 1;
                var head = new Point(Math.Clamp(x, 6, w - 6), cy);
                dc.DrawEllipse(new SolidColorBrush(glowColor), null, head, 9 * pulse, 9 * pulse);
                dc.DrawEllipse(Brushes.White, null, head, 4.2, 4.2);
                break;
            }
        }
    }

    private void EnsureBars(int count)
    {
        if (_bars.Length == count) return;
        var rng = new Random(_seed.GetHashCode());
        _bars = new float[count];
        var previous = 0.5f;
        for (var i = 0; i < count; i++)
        {
            // smoothed noise reads as a waveform rather than static
            previous = previous * 0.55f + (float)rng.NextDouble() * 0.45f;
            var envelope = (float)(0.55 + 0.45 * Math.Sin(i / (double)count * Math.PI)); // quieter at the ends
            _bars[i] = Math.Clamp(previous * 1.35f * envelope, 0.12f, 1f);
        }
    }
}
