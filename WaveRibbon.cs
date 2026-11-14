// D-Island (personal fork) - a flowing ribbon of layered waves along the bottom of the music pill, tinted by the cover's
// colours and swelling with the music (the way a phone's "now bar" shows playback).

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DIsland;

public enum WaveStyle { Ribbon, Silk, Tide, Pulse, Bars, Beads, Aurora, Comet, Pixels }

public sealed class WaveRibbon : FrameworkElement
{
    private WaveStyle _style = WaveStyle.Ribbon;
    public WaveStyle Style { get => _style; set { if (_style == value) return; _style = value; InvalidateVisual(); } }

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;
    private IReadOnlyList<Color> _colors = new[] { Color.FromRgb(0xE6, 0xEC, 0xFF), Color.FromRgb(0xA9, 0xB8, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF) };
    private double _target = 0.5, _level = 0.5;
    private bool _playing;

    public WaveRibbon()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => Update();
    }

    public IReadOnlyList<Color> Colors { set { _colors = value.Count > 0 ? value : _colors; InvalidateVisual(); } }

    /// <summary>0..1 loudness; null lets the ribbon breathe on its own.</summary>
    public double? Level { set { _target = value ?? double.NaN; } }

    public bool Playing { get => _playing; set { _playing = value; Update(); InvalidateVisual(); } }

    private void Update()
    {
        var run = _playing && IsVisible;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 20 || h < 5) return;
        var t = (DateTime.UtcNow - _start).TotalSeconds;

        // The loudness is heavily smoothed and squeezed into a narrow range: the ribbon breathes, it never jumps.
        var goal = double.IsNaN(_target) ? 0.5 : 0.3 + 0.5 * Math.Clamp(_target, 0, 1);
        _level += ((_playing ? goal : 0.15) - _level) * 0.03;

        var a = _colors[0];
        var b = _colors.Count > 1 ? _colors[1] : a;
        switch (_style)
        {
            case WaveStyle.Silk: DrawSilk(dc, w, h, t, a, b); return;
            case WaveStyle.Tide: DrawTide(dc, w, h, t, a, b); return;
            case WaveStyle.Pulse: DrawPulse(dc, w, h, t, a, b); return;
            case WaveStyle.Bars: DrawBars(dc, w, h, t, a, b); return;
            case WaveStyle.Beads: DrawBeads(dc, w, h, t, a, b); return;
            case WaveStyle.Aurora: DrawAurora(dc, w, h, t); return;
            case WaveStyle.Comet: DrawComet(dc, w, h, t, a, b); return;
            case WaveStyle.Pixels: DrawPixels(dc, w, h, t, a, b); return;
        }
        // two layers only: a soft main ribbon and a fainter one drifting the other way
        for (var k = 0; k < 2; k++)
        {
            var amplitude = (1.6 + 2.6 * _level) * (k == 0 ? 1 : 0.65);
            var wavelength = k == 0 ? 150 : 210;
            var phase = t * (k == 0 ? 0.9 : -0.6) + k * 2.1;
            var baseline = h - 3.2 - k * 1.2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, h + 1), true, true);
                for (double x = 0; x <= w + 3; x += 3)
                    ctx.LineTo(new Point(x, baseline - Math.Sin(x / wavelength * 2 * Math.PI + phase) * amplitude), true, false);
                ctx.LineTo(new Point(w, h + 1), true, false);
            }
            geometry.Freeze();
            var alpha = (byte)(k == 0 ? 0x70 : 0x36);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, a.R, a.G, a.B), 0),
                    new GradientStop(Color.FromArgb(alpha, a.R, a.G, a.B), 0.18),
                    new GradientStop(Color.FromArgb(alpha, b.R, b.G, b.B), 0.82),
                    new GradientStop(Color.FromArgb(0, b.R, b.G, b.B), 1),
                },
            };
            brush.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }
    }

    private static LinearGradientBrush Fade(Color a, Color b, byte alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, a.R, a.G, a.B), 0),
                new GradientStop(Color.FromArgb(alpha, a.R, a.G, a.B), 0.2),
                new GradientStop(Color.FromArgb(alpha, b.R, b.G, b.B), 0.8),
                new GradientStop(Color.FromArgb(0, b.R, b.G, b.B), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static StreamGeometry Line(double w, Func<double, double> y)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, y(0)), false, false);
            for (double x = 3; x <= w + 3; x += 3) ctx.LineTo(new Point(x, y(x)), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Silk: three hair-thin lines that weave across each other.</summary>
    private void DrawSilk(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        var mid = h - 4.2;
        for (var k = 0; k < 3; k++)
        {
            var amp = (1.4 + 2.2 * _level) * (1 - k * 0.18);
            var len = 110 + k * 38;
            var ph = t * (0.8 + k * 0.25) * (k % 2 == 0 ? 1 : -1) + k * 1.9;
            var pen = new Pen(Fade(k == 1 ? b : a, k == 1 ? a : b, (byte)(0xB0 - k * 0x30)), 1.2 - k * 0.15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Line(w, x => mid - Math.Sin(x / len * 2 * Math.PI + ph) * amp * Math.Sin(Math.Clamp(x / w, 0, 1) * Math.PI)));
        }
    }

    /// <summary>Tide: one big slow swell with a bright foam line riding its crest.</summary>
    private void DrawTide(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        double Y(double x) => h - 2.6 - (Math.Sin(x / 240 * 2 * Math.PI + t * 0.55) * 0.6 + Math.Sin(x / 95 * 2 * Math.PI - t * 0.9) * 0.25) * (1.8 + 2.6 * _level);
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(new Point(0, h + 1), true, true);
            for (double x = 0; x <= w + 3; x += 3) ctx.LineTo(new Point(x, Y(x)), true, false);
            ctx.LineTo(new Point(w, h + 1), true, false);
        }
        body.Freeze();
        dc.DrawGeometry(Fade(a, b, 0x4A), null, body);
        dc.DrawGeometry(null, new Pen(Fade(System.Windows.Media.Colors.White, System.Windows.Media.Colors.White, 0x9A), 1.0), Line(w, Y));
    }

    /// <summary>Pulse: a calm heartbeat line that spikes with the beat.</summary>
    private void DrawPulse(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        var mid = h - 4.0;
        var amp = 1.2 + 3.6 * _level;
        double Y(double x)
        {
            var u = ((x / 170.0) - t * 0.22) % 1.0;
            if (u < 0) u += 1;
            var spike = u < 0.06 ? -Math.Sin(u / 0.06 * Math.PI) * 1.0
                      : u < 0.1 ? Math.Sin((u - 0.06) / 0.04 * Math.PI) * 0.45 : 0;
            var drift = Math.Sin(x / 60 + t) * 0.18;
            return mid + (spike + drift) * amp;
        }
        var pen = new Pen(Fade(a, b, 0xC8), 1.3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, new Pen(Fade(a, b, 0x34), 3.6) { LineJoin = PenLineJoin.Round }, Line(w, Y)); // soft glow
        dc.DrawGeometry(null, pen, Line(w, Y));
    }

    /// <summary>Bars: a row of slim rounded bars rolling like a quiet spectrum.</summary>
    private void DrawBars(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        const double pitch = 6;
        var count = (int)(w / pitch);
        for (var i = 0; i < count; i++)
        {
            var x = i * pitch + 2;
            var env = Math.Sin(Math.Clamp(i / (double)count, 0, 1) * Math.PI);
            var v = 0.5 + 0.5 * Math.Sin(i * 0.55 + t * 2.1) * Math.Sin(i * 0.17 - t * 1.1);
            var bh = Math.Max(1.2, (1.4 + 7 * _level * v) * (0.35 + 0.65 * env));
            var c = DIsland.Services.ColorTools.Mix(a, b, i / (double)Math.Max(1, count - 1));
            var brush = new SolidColorBrush(Color.FromArgb((byte)(0x40 + 0x70 * env), c.R, c.G, c.B));
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - 1.5 - bh, 2.6, bh), 1.3, 1.3);
        }
    }

    /// <summary>Beads: a string of soft dots riding a wave, bigger where the wave is higher.</summary>
    private void DrawBeads(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        const double pitch = 9;
        var count = (int)(w / pitch);
        var mid = h - 4.6;
        for (var i = 0; i < count; i++)
        {
            var x = i * pitch + 4;
            var env = Math.Sin(Math.Clamp(i / (double)count, 0, 1) * Math.PI);
            var s = Math.Sin(x / 120 * 2 * Math.PI + t * 1.1);
            var y = mid - s * (1.4 + 2.4 * _level) * env;
            var r = (0.9 + 0.7 * (s * 0.5 + 0.5)) * (0.5 + 0.5 * env);
            var c = DIsland.Services.ColorTools.Mix(a, b, i / (double)Math.Max(1, count - 1));
            var brush = new SolidColorBrush(Color.FromArgb((byte)(0x50 + 0x90 * env), c.R, c.G, c.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), r, r);
        }
    }

    private Color PaletteAt(double f)
    {
        if (_colors.Count == 1) return _colors[0];
        f = Math.Clamp(f, 0, 1) * (_colors.Count - 1);
        var i = Math.Min((int)f, _colors.Count - 2);
        return DIsland.Services.ColorTools.Mix(_colors[i], _colors[i + 1], f - i);
    }

    /// <summary>Aurora: three soft filled waves, one in each of the cover's colours, drifting through each other.</summary>
    private void DrawAurora(DrawingContext dc, double w, double h, double t)
    {
        for (var k = 0; k < 3; k++)
        {
            var c = PaletteAt(k / 2.0);
            var amp = (1.6 + 2.8 * _level) * (1 - k * 0.15);
            var len = 120 + k * 55;
            var ph = t * (0.7 + k * 0.3) * (k == 1 ? -1 : 1) + k * 2.3;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, h + 1), true, true);
                for (double x = 0; x <= w + 3; x += 3)
                    ctx.LineTo(new Point(x, h - 3.4 - k * 0.8 - Math.Sin(x / len * 2 * Math.PI + ph) * amp), true, false);
                ctx.LineTo(new Point(w, h + 1), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Fade(c, c, (byte)(0x58 - k * 0x10)), null, g);
        }
    }

    /// <summary>Comet: a bright head sweeping along a gentle wave with a fading tail of beads behind it.</summary>
    private void DrawComet(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        double Y(double x) => h - 4.2 - Math.Sin(x / 90 * 2 * Math.PI + t * 1.2) * (1.4 + 2.2 * _level);
        dc.DrawGeometry(null, new Pen(Fade(a, b, 0x30), 1.0), Line(w, Y));
        var head = (t * 70) % (w + 80) - 20;
        for (var i = 0; i < 14; i++)
        {
            var x = head - i * 5;
            if (x < 0 || x > w) continue;
            var c = PaletteAt(Math.Clamp(x / w, 0, 1));
            var alpha = (byte)(230 * Math.Pow(1 - i / 14.0, 1.6));
            var r = 2.4 * (1 - i / 18.0);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, Y(x)), r, r);
        }
    }

    /// <summary>Pixels: a retro blocky equalizer made of small squares that hop in steps.</summary>
    private void DrawPixels(DrawingContext dc, double w, double h, double t, Color a, Color b)
    {
        const double cell = 4, gap = 1.4;
        var cols = (int)(w / (cell + gap));
        var rows = Math.Max(1, (int)(h / (cell + gap)));
        for (var i = 0; i < cols; i++)
        {
            var env = Math.Sin(Math.Clamp(i / (double)cols, 0, 1) * Math.PI);
            var v = 0.5 + 0.5 * Math.Sin(i * 0.5 + Math.Floor(t * 6) * 1.3) * Math.Sin(i * 0.21 - Math.Floor(t * 4));
            var filled = Math.Clamp((int)Math.Round((0.25 + 0.75 * _level * v) * rows * (0.4 + 0.6 * env)), 1, rows);
            var c = PaletteAt(i / (double)Math.Max(1, cols - 1));
            for (var r = 0; r < filled; r++)
            {
                var brush = new SolidColorBrush(Color.FromArgb((byte)(0x50 + 0x80 * env), c.R, c.G, c.B));
                brush.Freeze();
                dc.DrawRoundedRectangle(brush, null, new Rect(i * (cell + gap) + 1, h - (r + 1) * (cell + gap), cell, cell), 1, 1);
            }
        }
    }
}
