// Lumen (personal fork) - full-bleed living sky for the Home weather glance.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Full-bleed weather world for every sky type.
/// Lively = busy &amp; fast. Cinematic = sparse &amp; slow. Static = rich still frame (no timer).
/// </summary>
public sealed class WeatherScene : Grid
{
    private readonly Canvas _stage = new() { ClipToBounds = true };
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private WeatherKind _kind;
    private WeatherMotion _motion = WeatherMotion.Cinematic;
    private bool _day = true;
    private double _t;
    private double _intro = 2.0;
    private double _w = 220, _h = 88;
    private readonly List<Particle> _particles = new();
    private Ellipse? _sun, _glow;
    private readonly List<CloudLayer> _clouds = new();
    private Border? _flash;
    private TranslateTransform? _bloomTx;
    private double _wind;
    private readonly List<Line> _rays = new();

    // Density = how much stuff; Speed = how fast it moves. Static uses rich density, zero speed.
    private bool IsStatic => _motion == WeatherMotion.Static;
    private bool Lively => _motion == WeatherMotion.Lively;
    private double Density => _motion switch
    {
        WeatherMotion.Lively => 1.0,
        WeatherMotion.Cinematic => 0.42,
        _ => 1.0, // Static: full rich still
    };
    private double Speed => _motion switch
    {
        WeatherMotion.Lively => 1.55,
        WeatherMotion.Cinematic => 0.28,
        _ => 0,
    };

    private sealed class CloudLayer
    {
        public UIElement Visual = null!;
        public TranslateTransform Tx = null!;
        public double X, Y, Vx, Width;
    }

    private sealed class Particle
    {
        public UIElement Visual = null!;
        public double X, Y, Vx, Vy, Phase, Depth;
        public int Kind; // 0 rain, 1 snow, 2 leaf, 3 mist, 4 star
    }

    public WeatherScene()
    {
        ClipToBounds = true;
        Children.Add(_stage);
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width < 2 || e.NewSize.Height < 2) return;
            if (Math.Abs(e.NewSize.Width - _w) < 3 && Math.Abs(e.NewSize.Height - _h) < 3)
            {
                EnsureRunning();
                return;
            }
            _w = e.NewSize.Width;
            _h = e.NewSize.Height;
            _stage.Width = _w;
            _stage.Height = _h;
            if (_stage.Children.Count > 0) Rebuild();
            EnsureRunning();
        };
        _tick.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) EnsureRunning();
            else _tick.Stop();
        };
        Loaded += (_, _) => EnsureRunning();
        Unloaded += (_, _) => _tick.Stop();
    }

    /// <summary>Start the clock even if weather didn't change — first show used to paint a still frame until you left Home.</summary>
    public void EnsureRunning()
    {
        if (!IsVisible && !IsLoaded) return;
        if (_stage.Children.Count == 0 && _w >= 8) Rebuild();
        if (IsStatic)
        {
            _tick.Stop();
            return;
        }
        _tick.Interval = TimeSpan.FromMilliseconds(Lively ? 28 : 50);
        if (!_tick.IsEnabled) _tick.Start();
    }

    public void Show(WeatherKind kind, bool isDay, WeatherMotion motion = WeatherMotion.Cinematic)
    {
        var same = kind == _kind && isDay == _day && motion == _motion && _stage.Children.Count > 0;
        _kind = kind;
        _day = isDay;
        _motion = motion;
        if (!same)
        {
            _intro = Lively ? 1.2 : 2.4;
            _t = 0;
            Rebuild();
        }
        EnsureRunning();
    }

    private int Count(int livelyN) => Math.Max(1, (int)Math.Round(livelyN * Density));

    private void Rebuild()
    {
        if (_w < 8 || _h < 8) return;
        _stage.Children.Clear();
        _particles.Clear();
        _clouds.Clear();
        _rays.Clear();
        _sun = _glow = null;
        _flash = null;
        _bloomTx = null;
        _wind = (_kind is WeatherKind.Rain or WeatherKind.Thunder ? 0.55
              : _kind is WeatherKind.Drizzle or WeatherKind.Snow ? 0.28
              : 0.12) * (Lively ? 1.35 : IsStatic ? 1.0 : 0.55);

        var (top, mid, bot) = SkyTriple(_kind, _day);
        _stage.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0.2, 0),
            EndPoint = new Point(0.8, 1),
            GradientStops =
            {
                new GradientStop(top, 0),
                new GradientStop(mid, 0.5),
                new GradientStop(bot, 1),
            },
        };

        var bloom = new Ellipse
        {
            Width = _w * 0.9, Height = _h * 1.15,
            Fill = new RadialGradientBrush(
                Color.FromArgb(_day ? (byte)0x70 : (byte)0x44, top.R, top.G, top.B),
                Color.FromArgb(0x00, top.R, top.G, top.B)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _bloomTx = new TranslateTransform();
        bloom.RenderTransform = _bloomTx;
        Canvas.SetLeft(bloom, _day ? _w * 0.22 : -_w * 0.08);
        Canvas.SetTop(bloom, -_h * 0.5);
        _stage.Children.Add(bloom);

        var accent = AccentColor(_kind, _day);
        var wash = new Ellipse
        {
            Width = _w * 0.75, Height = _h,
            Fill = new RadialGradientBrush(
                Color.FromArgb(0x3A, accent.R, accent.G, accent.B),
                Color.FromArgb(0x00, accent.R, accent.G, accent.B)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(wash, _w * 0.3);
        Canvas.SetTop(wash, _h * 0.1);
        _stage.Children.Add(wash);

        var ground = new Ellipse
        {
            Width = _w * 1.45, Height = _h * 0.55,
            Fill = new RadialGradientBrush(
                Color.FromArgb(0x60, bot.R, bot.G, bot.B),
                Color.FromArgb(0x00, bot.R, bot.G, bot.B)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ground, -_w * 0.2);
        Canvas.SetTop(ground, _h * 0.62);
        _stage.Children.Add(ground);

        switch (_kind)
        {
            case WeatherKind.Clear:
                BuildSunMoon();
                if (_day)
                {
                    AddSunRays(Lively ? 10 : Count(10));
                    AddCloud(0.28, 0.14, 0.08, 0.7, far: true);
                    if (Lively || IsStatic) AddCloud(0.35, 0.22, 0.72, 0.55);
                    AddHaze(Count(5));
                }
                else
                {
                    AddStars(Count(22));
                    if (Lively) AddStars(8); // extra twinklers only when lively
                }
                break;
            case WeatherKind.PartlyCloudy:
                BuildSunMoon();
                if (_day) AddSunRays(Count(6));
                AddCloud(0.4, 0.06, 0.12, 0.95, far: true);
                AddCloud(0.72, 0.26, 0.48, 1.1);
                if (Lively || IsStatic) AddCloud(0.5, 0.38, 0.78, 0.7);
                AddHaze(Count(3));
                break;
            case WeatherKind.Cloudy:
                AddCloud(0.45, 0.02, 0.02, 1.05, far: true);
                AddCloud(0.78, 0.16, 0.32, 1.2);
                AddCloud(0.38, 0.36, 0.18, 0.85);
                if (Lively || IsStatic) AddCloud(0.55, 0.28, 0.62, 0.9);
                AddHaze(Count(7));
                break;
            case WeatherKind.Fog:
                AddCloud(0.3, 0.1, 0.2, 1.0, far: true);
                AddFogBands();
                AddHaze(Count(10));
                break;
            case WeatherKind.Drizzle:
                AddCloud(0.5, 0.0, 0.5, 1.0, far: true);
                AddCloud(0.82, 0.08, 0.18, 1.2);
                AddCloud(0.65, 0.2, 0.42, 0.95);
                SpawnRain(Count(22), far: true, soft: true);
                SpawnRain(Count(20), far: false, soft: true);
                AddHaze(Count(5));
                break;
            case WeatherKind.Rain:
                AddCloud(0.55, -0.04, 0.45, 1.15, far: true);
                AddCloud(0.9, 0.04, 0.1, 1.3);
                AddCloud(0.75, 0.16, 0.35, 1.05);
                if (Lively || IsStatic) AddCloud(0.6, 0.28, 0.7, 0.85);
                SpawnRain(Count(32), far: true, soft: false);
                SpawnRain(Count(30), far: false, soft: false);
                AddHaze(Count(4));
                break;
            case WeatherKind.Snow:
                AddCloud(0.55, 0.0, 0.12, 1.05, far: true);
                AddCloud(0.48, 0.2, 0.4, 0.95);
                if (Lively || IsStatic) AddCloud(0.4, 0.32, 0.7, 0.8);
                SpawnSnow(Count(18), far: true);
                SpawnSnow(Count(16), far: false);
                break;
            case WeatherKind.Thunder:
                AddCloud(0.88, -0.06, 0.0, 1.3, far: true);
                AddCloud(0.85, 0.1, 0.28, 1.15);
                if (Lively || IsStatic) AddCloud(0.7, 0.22, 0.55, 1.0);
                SpawnRain(Count(24), far: true, soft: false);
                SpawnRain(Count(22), far: false, soft: false);
                _flash = new Border { Width = _w, Height = _h, Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
                _stage.Children.Add(_flash);
                break;
        }

        var month = DateTime.Now.Month;
        if (_day && _kind is WeatherKind.Clear or WeatherKind.PartlyCloudy && month is >= 9 and <= 11)
            SpawnLeaves(Count(12));

        _stage.Children.Add(new Border
        {
            Width = _w, Height = _h * 0.4,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
            IsHitTestVisible = false,
        });

        _stage.Children.Add(new Border
        {
            Width = _w * 0.58, Height = _h,
            Background = new LinearGradientBrush(
                Color.FromArgb(0xA8, 0x00, 0x00, 0x00),
                Color.FromArgb(0x00, 0x00, 0x00, 0x00), 0)
            { EndPoint = new Point(1, 0) },
            IsHitTestVisible = false,
        });
    }

    private static Color AccentColor(WeatherKind kind, bool day) => kind switch
    {
        WeatherKind.Clear when day => Color.FromRgb(0xFF, 0xC0, 0x40),
        WeatherKind.Clear => Color.FromRgb(0x8A, 0x7A, 0xFF),
        WeatherKind.Rain or WeatherKind.Drizzle => Color.FromRgb(0x4A, 0xC8, 0xFF),
        WeatherKind.Thunder => Color.FromRgb(0xA0, 0x80, 0xFF),
        WeatherKind.Snow => Color.FromRgb(0xC0, 0xE8, 0xFF),
        _ => day ? Color.FromRgb(0x70, 0xC0, 0xFF) : Color.FromRgb(0x70, 0x70, 0xB0),
    };

    private static (Color Top, Color Mid, Color Bot) SkyTriple(WeatherKind kind, bool day) => kind switch
    {
        WeatherKind.Clear when day => (Rgb(0x2F, 0x7E, 0xFF), Rgb(0x6A, 0xB8, 0xFF), Rgb(0xC8, 0xE8, 0xFF)),
        WeatherKind.Clear => (Rgb(0x0E, 0x12, 0x32), Rgb(0x28, 0x20, 0x52), Rgb(0x5C, 0x38, 0x6E)),
        WeatherKind.PartlyCloudy when day => (Rgb(0x3E, 0x90, 0xE0), Rgb(0x88, 0xC0, 0xF0), Rgb(0xD8, 0xEC, 0xFA)),
        WeatherKind.PartlyCloudy => (Rgb(0x16, 0x1A, 0x36), Rgb(0x30, 0x2A, 0x50), Rgb(0x54, 0x44, 0x68)),
        WeatherKind.Thunder => (Rgb(0x18, 0x1C, 0x2C), Rgb(0x32, 0x38, 0x4E), Rgb(0x4E, 0x58, 0x6C)),
        WeatherKind.Rain => (Rgb(0x14, 0x2E, 0x58), Rgb(0x1E, 0x52, 0x7A), Rgb(0x2A, 0x72, 0x92)),
        WeatherKind.Drizzle => (Rgb(0x18, 0x3A, 0x60), Rgb(0x28, 0x5E, 0x82), Rgb(0x38, 0x80, 0x9C)),
        WeatherKind.Snow => (Rgb(0x5A, 0x6E, 0x88), Rgb(0x9A, 0xB0, 0xC8), Rgb(0xE0, 0xEC, 0xF6)),
        WeatherKind.Fog => (Rgb(0x6A, 0x74, 0x80), Rgb(0x98, 0xA2, 0xAC), Rgb(0xC8, 0xD0, 0xD6)),
        _ => (Rgb(0x4A, 0x5A, 0x6C), Rgb(0x6A, 0x7A, 0x8C), Rgb(0x90, 0x9E, 0xAC)),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private void BuildSunMoon()
    {
        var size = _day ? Math.Min(34, _h * 0.52) : Math.Min(26, _h * 0.42);
        _glow = new Ellipse
        {
            Width = size * 2.15, Height = size * 2.15,
            Fill = new RadialGradientBrush(
                _day ? Color.FromArgb(0x80, 0xFF, 0xC4, 0x18) : Color.FromArgb(0x60, 0xC8, 0xD8, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
        };
        _sun = new Ellipse
        {
            Width = size, Height = size,
            Fill = _day
                ? new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.32, 0.28),
                    GradientStops =
                    {
                        new GradientStop(Rgb(0xFF, 0xFA, 0xD8), 0),
                        new GradientStop(Rgb(0xFF, 0xCC, 0x28), 0.5),
                        new GradientStop(Rgb(0xFF, 0x84, 0x08), 1),
                    },
                }
                : new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.32, 0.28),
                    GradientStops =
                    {
                        new GradientStop(Rgb(0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(Rgb(0xD0, 0xDC, 0xF0), 0.7),
                        new GradientStop(Rgb(0x98, 0xA8, 0xC8), 1),
                    },
                },
        };
        var gx = _w * 0.64;
        var gy = _h * 0.1;
        Canvas.SetLeft(_glow, gx - size * 0.55);
        Canvas.SetTop(_glow, gy - size * 0.55);
        Canvas.SetLeft(_sun, gx);
        Canvas.SetTop(_sun, gy);
        _stage.Children.Add(_glow);
        _stage.Children.Add(_sun);
    }

    private void AddSunRays(int n)
    {
        if (n <= 0 || !_day) return;
        var cx = _w * 0.64 + (_sun?.Width ?? 20) * 0.5;
        var cy = _h * 0.1 + (_sun?.Height ?? 20) * 0.5;
        for (var i = 0; i < n; i++)
        {
            var ang = i * (Math.PI * 2 / n) - Math.PI * 0.15;
            var inner = (_sun?.Width ?? 20) * 0.7;
            var outer = inner + (Lively ? 18 : 12);
            var ray = new Line
            {
                X1 = Math.Cos(ang) * inner, Y1 = Math.Sin(ang) * inner,
                X2 = Math.Cos(ang) * outer, Y2 = Math.Sin(ang) * outer,
                Stroke = new SolidColorBrush(Color.FromArgb(Lively ? (byte)0x55 : (byte)0x30, 0xFF, 0xE0, 0x80)),
                StrokeThickness = Lively ? 1.6 : 1.1,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ray, cx);
            Canvas.SetTop(ray, cy);
            _stage.Children.Add(ray);
            _rays.Add(ray);
        }
    }

    private void AddCloud(double opacity, double yFrac, double xFrac, double scale, bool far = false)
    {
        var g = new Canvas { Opacity = far ? opacity * 0.85 : opacity };
        var unit = Math.Max(10, _h * 0.22) * scale;
        void Puff(double px, double py, double s, byte a, bool dark = false)
        {
            var aa = far ? (byte)(a * 0.7) : a;
            Color c;
            if (_day)
                c = dark ? Color.FromArgb(aa, 0xA8, 0xB8, 0xCC) : Color.FromArgb(aa, 0xFF, 0xFF, 0xFF);
            else
                c = dark ? Color.FromArgb(aa, 0x48, 0x50, 0x68) : Color.FromArgb(aa, 0xA0, 0xAC, 0xC0);
            var e = new Ellipse
            {
                Width = unit * s, Height = unit * s * 0.6,
                Fill = new RadialGradientBrush(c, Color.FromArgb(0x00, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(e, px); Canvas.SetTop(e, py);
            g.Children.Add(e);
        }
        Puff(0, unit * 0.35, 1.0, 0xC8, dark: true);
        Puff(unit * 0.55, 0, 1.5, 0xF0);
        Puff(unit * 1.35, unit * 0.28, 1.15, 0xDC);
        Puff(unit * 0.35, unit * 0.45, 0.9, 0xA0, dark: true);
        var width = unit * 2.6;
        var x = _w * xFrac;
        var y = _h * yFrac;
        var tx = new TranslateTransform(x, y);
        g.RenderTransform = tx;
        // Far = slower. Lively races; cinematic creeps.
        var vx = (far ? 2.2 : 4.5) * (0.7 + _wind) * Speed;
        _clouds.Add(new CloudLayer { Visual = g, Tx = tx, X = x, Y = y, Vx = vx, Width = width });
        _stage.Children.Add(g);
    }

    private void AddHaze(int n)
    {
        var rng = new Random(21);
        for (var i = 0; i < n; i++)
        {
            var s = _h * (0.14 + rng.NextDouble() * 0.22);
            var mist = new Ellipse
            {
                Width = s * 1.9, Height = s * 0.5,
                Fill = new RadialGradientBrush(
                    Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
            };
            var x = rng.NextDouble() * _w;
            var y = _h * (0.35 + rng.NextDouble() * 0.5);
            Canvas.SetLeft(mist, x); Canvas.SetTop(mist, y);
            _stage.Children.Add(mist);
            _particles.Add(new Particle
            {
                Visual = mist, X = x, Y = y, Kind = 3, Depth = 0.3,
                Vx = (1.2 + rng.NextDouble() * 1.5) * Speed, Phase = rng.NextDouble() * 6,
            });
        }
    }

    private void AddFogBands()
    {
        var bands = Count(4);
        for (var i = 0; i < bands; i++)
        {
            var band = new Border
            {
                Width = _w * 1.25, Height = _h * 0.16,
                CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x48, 0xF0, 0xF4, 0xF8), 0) { EndPoint = new Point(1, 0) },
                Opacity = 0.5,
            };
            var x = -_w * 0.12 + i * 14;
            var y = _h * (0.22 + i * 0.18);
            Canvas.SetLeft(band, x); Canvas.SetTop(band, y);
            _stage.Children.Add(band);
            _particles.Add(new Particle
            {
                Visual = band, X = x, Y = y,
                Vx = (3 + i * 0.7) * Speed, Kind = 3, Depth = 0.4, Phase = i,
            });
        }
    }

    private void AddStars(int n)
    {
        var rng = new Random(7 + n);
        for (var i = 0; i < n; i++)
        {
            var s = 1.15 + rng.NextDouble() * 1.9;
            var star = new Ellipse
            {
                Width = s, Height = s,
                Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
            };
            var x = rng.NextDouble() * _w;
            var y = rng.NextDouble() * _h * 0.55;
            Canvas.SetLeft(star, x); Canvas.SetTop(star, y);
            _stage.Children.Add(star);
            _particles.Add(new Particle { Visual = star, Phase = rng.NextDouble() * 6, Kind = 4 });
        }
    }

    private void SpawnRain(int n, bool far, bool soft)
    {
        var rng = new Random(far ? 11 : 19);
        var depth = far ? 0.25 : 0.85;
        // Cinematic: longer softer streaks; Lively: short hard drops
        var lenMul = Lively ? 1.0 : IsStatic ? 1.0 : 1.7;
        var thickMul = Lively ? 1.0 : IsStatic ? 1.0 : 0.85;
        for (var i = 0; i < n; i++)
        {
            var len = (soft
                ? (far ? 4 : 6) + rng.NextDouble() * (far ? 3 : 4)
                : (far ? 6 : 9) + rng.NextDouble() * (far ? 4 : 6)) * lenMul;
            var alpha = far ? (byte)0x66 : (soft ? (byte)0xAA : (byte)0xDD);
            var drop = new Line
            {
                X1 = 0, Y1 = 0,
                X2 = (soft ? 0.8 : 1.8) * (0.5 + depth) * (Lively ? 1.2 : 0.7),
                Y2 = len,
                Stroke = new LinearGradientBrush(
                    Color.FromArgb(0x00, 0x70, 0xD0, 0xFF),
                    Color.FromArgb(alpha, 0xC0, 0xEE, 0xFF), 90),
                StrokeThickness = (far ? 1.0 : (soft ? 1.35 : 1.7)) * thickMul,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = far ? 0.45 : 0.7 + rng.NextDouble() * 0.25,
            };
            var x = rng.NextDouble() * (_w + 20);
            var y = rng.NextDouble() * _h;
            Canvas.SetLeft(drop, x); Canvas.SetTop(drop, y);
            _stage.Children.Add(drop);
            var speed = (soft ? 1.2 : 2.4) * Speed;
            _particles.Add(new Particle
            {
                Visual = drop, X = x, Y = y, Kind = 0, Depth = depth,
                Vy = speed * (0.55 + depth) + rng.NextDouble() * 0.8 * Speed,
                Vx = _wind * (0.4 + depth) * Speed + (far ? 0.1 : 0.35) * Speed,
                Phase = rng.NextDouble() * 6,
            });
        }
    }

    private void SpawnSnow(int n, bool far)
    {
        var rng = new Random(far ? 13 : 23);
        var depth = far ? 0.3 : 0.85;
        for (var i = 0; i < n; i++)
        {
            var s = (far ? 1.8 : 2.6) + rng.NextDouble() * (far ? 2.2 : 3.0);
            if (!Lively && !IsStatic) s *= 1.25; // cinematic flakes bigger / softer
            var flake = new Ellipse
            {
                Width = s, Height = s,
                Fill = new RadialGradientBrush(
                    Color.FromArgb(far ? (byte)0xAA : (byte)0xFF, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xD0, 0xE8, 0xFF)),
                Opacity = far ? 0.5 : 0.9,
            };
            var x = rng.NextDouble() * _w;
            var y = rng.NextDouble() * _h;
            Canvas.SetLeft(flake, x); Canvas.SetTop(flake, y);
            _stage.Children.Add(flake);
            _particles.Add(new Particle
            {
                Visual = flake, X = x, Y = y, Kind = 1, Depth = depth,
                Vy = (0.25 * (0.5 + depth) + rng.NextDouble() * 0.35) * Speed,
                Vx = (_wind * 0.4 + (rng.NextDouble() - 0.5) * 0.35) * Speed,
                Phase = rng.NextDouble() * 6,
            });
        }
    }

    private void SpawnLeaves(int n)
    {
        var rng = new Random(17);
        var colors = new[] { Rgb(0xE8, 0x6A, 0x17), Rgb(0xD4, 0x3A, 0x2A), Rgb(0xF0, 0xC0, 0x40), Rgb(0xC4, 0x5A, 0x20), Rgb(0xA8, 0x3A, 0x18) };
        for (var i = 0; i < n; i++)
        {
            var leaf = new Path
            {
                Data = Geometry.Parse("M0,2 C1,-1 4,-1 5,2 C4,5 1,5 0,2 Z"),
                Fill = new SolidColorBrush(colors[rng.Next(colors.Length)]),
                Stretch = Stretch.Fill,
                Width = 7.5, Height = 4.5,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(rng.NextDouble() * 360),
                Opacity = 0.85,
            };
            var x = rng.NextDouble() * _w;
            var y = -rng.NextDouble() * _h;
            Canvas.SetLeft(leaf, x); Canvas.SetTop(leaf, y);
            _stage.Children.Add(leaf);
            _particles.Add(new Particle
            {
                Visual = leaf, X = x, Y = y, Kind = 2, Depth = 0.7,
                Vy = (0.35 + rng.NextDouble() * 0.4) * Speed,
                Vx = (0.25 + _wind * 0.8 + rng.NextDouble() * 0.35) * Speed,
                Phase = rng.NextDouble() * 6,
            });
        }
    }

    private void Step()
    {
        if (IsStatic) { _tick.Stop(); return; }

        var dt = Lively ? 0.033 : 0.05;
        _t += dt;
        if (_intro > 0) _intro -= dt;

        if (_sun is not null && _glow is not null)
        {
            var u = _intro > 0 ? 1 - Math.Clamp(_intro / (Lively ? 1.2 : 2.4), 0, 1) : 1;
            var rise = 1 - Math.Pow(1 - u, 3);
            var baseY = _h * 0.1;
            var startY = _h * 0.78;
            var y = startY + (baseY - startY) * rise;
            // Lively: obvious bob. Cinematic: barely drifts.
            if (u >= 1) y += Math.Sin(_t * (Lively ? 0.9 : 0.18)) * (Lively ? 2.2 : 0.45);
            Canvas.SetTop(_sun, y);
            Canvas.SetTop(_glow, y - _sun.Height * 0.55);
            var breath = 1 + Math.Sin(_t * (Lively ? 1.1 : 0.25)) * (Lively ? 0.045 : 0.012);
            _glow.RenderTransform = new ScaleTransform(breath, breath, _glow.Width / 2, _glow.Height / 2);
            _sun.Opacity = 0.65 + 0.35 * rise;

            // Sun rays rotate — lively spins, cinematic creeps.
            if (_rays.Count > 0)
            {
                var spin = _t * (Lively ? 0.35 : 0.06);
                var cx = Canvas.GetLeft(_sun) + _sun.Width / 2;
                var cy = y + _sun.Height / 2;
                for (var i = 0; i < _rays.Count; i++)
                {
                    var ang = i * (Math.PI * 2 / _rays.Count) - Math.PI * 0.15 + spin;
                    var inner = _sun.Width * 0.7;
                    var outer = inner + (Lively ? 18 : 12);
                    var ray = _rays[i];
                    ray.X1 = Math.Cos(ang) * inner;
                    ray.Y1 = Math.Sin(ang) * inner;
                    ray.X2 = Math.Cos(ang) * outer;
                    ray.Y2 = Math.Sin(ang) * outer;
                    Canvas.SetLeft(ray, cx);
                    Canvas.SetTop(ray, cy);
                    ray.Opacity = 0.35 + 0.25 * Math.Sin(_t * (Lively ? 2.2 : 0.5) + i);
                }
            }
        }

        if (_bloomTx is not null)
            _bloomTx.Y = Math.Sin(_t * (Lively ? 0.55 : 0.12)) * (Lively ? 2.8 : 0.7);

        foreach (var c in _clouds)
        {
            c.X += c.Vx * dt;
            if (c.X > _w + 8) c.X = -c.Width - 8;
            if (c.X < -c.Width - 8) c.X = _w + 8;
            c.Tx.X = c.X;
            var bob = (Lively ? 2.4 : 0.55) * Math.Sin(_t * (Lively ? 0.55 : 0.12) + c.X * 0.01);
            c.Tx.Y = c.Y + bob;
        }

        // Thunder: lively flashes often; cinematic rare soft flashes.
        if (_kind == WeatherKind.Thunder && _flash is not null)
        {
            var chance = Lively ? 0.028 : 0.006;
            if (Random.Shared.NextDouble() < chance)
                _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(Lively ? 0.65 : 0.35, 0, TimeSpan.FromMilliseconds(Lively ? 120 : 220)));
        }

        foreach (var p in _particles)
        {
            if (p.Kind == 4)
            {
                if (p.Visual is Ellipse star)
                {
                    var twinkle = Lively ? 2.4 : 0.7;
                    star.Opacity = 0.2 + 0.55 * (0.5 + 0.5 * Math.Sin(_t * twinkle + p.Phase));
                }
                continue;
            }

            p.Phase += dt;
            var sway = p.Kind is 1 or 2 ? Math.Sin(p.Phase * (Lively ? 1.4 : 0.5)) * (0.25 + p.Depth * 0.35) * (Lively ? 1.4 : 0.6) : 0;
            p.X += (p.Vx + sway) * (Lively ? 1.0 : 1.0); // Vx already scaled by Speed at spawn
            // For cinematic rain with Speed baked in, still advance; Static never reaches Step.
            p.Y += p.Vy;

            // Scale position step if Speed is low — particles already have Speed in Vx/Vy.
            if (p.Y > _h + 10) { p.Y = -8; p.X = Random.Shared.NextDouble() * _w; }
            if (p.X > _w + 18) p.X = -12;
            if (p.X < -18) p.X = _w + 8;

            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);
            if (p.Visual.RenderTransform is RotateTransform rot)
                rot.Angle += (p.Kind == 2 ? 28 : 8) * Math.Max(0.15, Math.Abs(p.Vx)) * dt * (Lively ? 1.5 : 0.4);
        }
    }
}
