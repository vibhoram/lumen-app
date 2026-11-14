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
        if (w < 20 || h < 6) return;
        var t = (DateTime.UtcNow - _start).TotalSeconds;
        var goal = double.IsNaN(_target) ? 0.5 + 0.28 * Math.Sin(t * 1.3) : _target;
        _level += ((_playing ? goal : 0.1) - _level) * (goal > _level ? 0.45 : 0.16); // quick attack, slower fall

        for (var k = 0; k < 3; k++)
        {
            var color = _colors[k % _colors.Count];
            var amplitude = (1.4 + 11 * _level) * (1 - 0.22 * k);
            var wavelength = 70 + 38 * k;
            var phase = t * (1.7 + 0.8 * k + 2.2 * _level) * (k % 2 == 0 ? 1 : -1) + k * 1.9;
            var baseline = h - 2.5 - k * 1.4;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, h + 1), true, true);
                for (double x = 0; x <= w + 2; x += 2.5)
                {
                    var edge = Math.Min(1, Math.Min(x, w - x) / 26); // calm at both ends
                    ctx.LineTo(new Point(x, baseline - Math.Sin(x / wavelength * 2 * Math.PI + phase) * amplitude * edge), true, false);
                }
                ctx.LineTo(new Point(w, h + 1), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(0xA0 - 0x28 * k), color.R, color.G, color.B)), null, geometry);
        }
    }
}
