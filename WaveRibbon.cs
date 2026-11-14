// D-Island (personal fork) - a flowing ribbon of layered waves along the bottom of the music pill, tinted by the cover's
// colours and swelling with the music (the way a phone's "now bar" shows playback).

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DIsland;

public sealed class WaveRibbon : FrameworkElement
{
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
}
