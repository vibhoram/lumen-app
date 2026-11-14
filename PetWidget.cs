// D-Island (personal fork) - a little desktop pet that wanders beneath the island: walks, sits, sleeps, dances to the
// music and hops when you click it. Drawn with vector shapes (four species), animated by a light timer that only
// runs while the pet is on screen.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DIsland;

public enum PetKind { Cat, Dog, Bunny, Robot }

public sealed class PetWidget : Canvas
{
    public const double SpriteWidth = 44;
    public const double SpriteHeight = 34;

    private enum Mood { Walk, Idle, Sleep, Dance, Hop }

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _rng = new();
    private readonly Canvas _sprite = new() { Width = SpriteWidth, Height = SpriteHeight };
    private readonly ScaleTransform _flip = new(1, 1, SpriteWidth / 2, SpriteHeight);
    private readonly TranslateTransform _bob = new();
    private readonly TranslateTransform _move = new();
    private readonly RotateTransform _headNod = new(0, 29, 22);
    private readonly RotateTransform _tailWag = new(0, 8, 21);
    private readonly ScaleTransform _squash = new(1, 1, SpriteWidth / 2, SpriteHeight - 2);
    private readonly List<(Rectangle Leg, RotateTransform Rot, double Phase)> _legs = new();
    private readonly Ellipse _eye = new() { Width = 2.4, Height = 2.6 };
    private readonly ScaleTransform _blink = new(1, 1, 1.2, 1.3);
    private readonly TextBlock _zzz = new() { Text = "z", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)), Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock _heart = new() { Text = "♥", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x7A)), Opacity = 0, IsHitTestVisible = false };

    private PetKind _kind = PetKind.Cat;
    private Mood _mood = Mood.Walk;
    private double _x, _minX, _maxX = 200, _dir = 1, _phase, _moodLeft = 3, _hopT;
    private DateTime _last = DateTime.UtcNow;
    private double _nextBlink = 2;
    private bool _music;

    public PetWidget()
    {
        Width = SpriteWidth;
        Height = SpriteHeight;
        Children.Add(_sprite);
        Cursor = System.Windows.Input.Cursors.Hand;
        Background = Brushes.Transparent; // the whole sprite box is clickable
        _sprite.RenderTransform = new TransformGroup { Children = { _squash, _flip, _bob } };
        RenderTransform = _move;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Hop(); };
        _timer.Tick += (_, _) => Tick();
        Build();
    }

    public PetKind Kind
    {
        get => _kind;
        set { if (value == _kind) return; _kind = value; Build(); }
    }

    /// <summary>True while music plays: the pet dances when it has nothing else to do.</summary>
    public bool Music
    {
        get => _music;
        set { _music = value; if (value && _mood is Mood.Idle or Mood.Walk) SetMood(Mood.Dance); else if (!value && _mood == Mood.Dance) SetMood(Mood.Idle); }
    }

    /// <summary>The range (in this element's parent) the pet may walk across.</summary>
    public void SetRange(double min, double max)
    {
        _minX = min;
        _maxX = Math.Max(min + 1, max - SpriteWidth);
        _x = Math.Clamp(_x, _minX, _maxX);
    }

    public bool Running
    {
        get => _timer.IsEnabled;
        set
        {
            if (value && !_timer.IsEnabled) { _last = DateTime.UtcNow; _timer.Start(); }
            else if (!value && _timer.IsEnabled) _timer.Stop();
        }
    }

    public void Hop()
    {
        _hopT = 0;
        _heart.Opacity = 1;
        SetMood(Mood.Hop);
    }

    // ------------------------------------------------------------------ drawing

    private static Brush Solid(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void Build()
    {
        _sprite.Children.Clear();
        _legs.Clear();
        var (body, dark, belly, earInner) = _kind switch
        {
            PetKind.Dog => (Solid("#D9A86C"), Solid("#8B5A2B"), Solid("#F0D5A8"), Solid("#8B5A2B")),
            PetKind.Bunny => (Solid("#F4F4F8"), Solid("#D8D8E2"), Solid("#FFFFFF"), Solid("#FFB3C7")),
            PetKind.Robot => (Solid("#9FB3C8"), Solid("#6B7F94"), Solid("#C9D6E3"), Solid("#2DD4FF")),
            _ => (Solid("#E9B27A"), Solid("#C98A52"), Solid("#F7DDBE"), Solid("#FFB3C7")),
        };

        Add(new Ellipse { Width = 26, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) }, 9, SpriteHeight - 4);

        // tail (behind the body)
        var tail = new Canvas { RenderTransform = _tailWag };
        switch (_kind)
        {
            case PetKind.Cat:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,21 C 0,20 -2,12 3,9"), Stroke = body, StrokeThickness = 3.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case PetKind.Dog:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,21 L 3,14"), Stroke = body, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case PetKind.Bunny:
                tail.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = belly, Margin = new Thickness(1, 17, 0, 0) });
                break;
            default:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,23 L 3,23"), Stroke = dark, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round });
                break;
        }
        _sprite.Children.Add(tail);

        // legs: far pair slightly darker, then the body over them
        AddLeg(dark, 10, 0);
        AddLeg(dark, 24, Math.PI);
        _sprite.Children.Add(MakeBody(body, belly));
        AddLeg(body, 14, Math.PI);
        AddLeg(body, 29, 0);

        // head group nods independently
        var head = new Canvas { RenderTransform = _headNod };
        if (_kind == PetKind.Robot)
        {
            head.Children.Add(Place(new Rectangle { Width = 15, Height = 13, RadiusX = 3.5, RadiusY = 3.5, Fill = body, Stroke = dark, StrokeThickness = 0.8 }, 22, 10));
            head.Children.Add(Place(new Line { X1 = 29.5, Y1 = 10, X2 = 29.5, Y2 = 4.5, Stroke = dark, StrokeThickness = 1.2 }, 0, 0));
            head.Children.Add(Place(new Ellipse { Width = 3.4, Height = 3.4, Fill = earInner }, 27.8, 2.2));
        }
        else
        {
            head.Children.Add(Place(new Ellipse { Width = 15, Height = 14, Fill = body }, 22, 10));
            switch (_kind)
            {
                case PetKind.Cat:
                    head.Children.Add(Place(new Polygon { Points = Points("0,7 1.2,0 5,4.6"), Fill = body }, 23.5, 7));
                    head.Children.Add(Place(new Polygon { Points = Points("0,4.6 3.8,0 5,7"), Fill = body }, 31, 7));
                    head.Children.Add(Place(new Polygon { Points = Points("1,5.4 1.8,2 3.6,4.2"), Fill = earInner }, 24, 8.4));
                    head.Children.Add(Place(new Polygon { Points = Points("0.4,4.2 2.2,2 3,5.4"), Fill = earInner }, 32, 8.4));
                    break;
                case PetKind.Dog:
                    head.Children.Add(Place(new Ellipse { Width = 5.5, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(14, 2.7, 1) }, 20.6, 10));
                    head.Children.Add(Place(new Ellipse { Width = 5.5, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(-14, 2.7, 1) }, 33.6, 10.5));
                    break;
                case PetKind.Bunny:
                    head.Children.Add(Place(new Ellipse { Width = 4.6, Height = 15, Fill = body, Stroke = dark, StrokeThickness = 0.6, RenderTransform = new RotateTransform(-8, 2.3, 14) }, 24.5, -4));
                    head.Children.Add(Place(new Ellipse { Width = 4.6, Height = 15, Fill = body, Stroke = dark, StrokeThickness = 0.6, RenderTransform = new RotateTransform(10, 2.3, 14) }, 31, -3.5));
                    head.Children.Add(Place(new Ellipse { Width = 2, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(-8, 1, 10) }, 25.8, -1.5));
                    head.Children.Add(Place(new Ellipse { Width = 2, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(10, 1, 10) }, 32.3, -1));
                    break;
            }
        }
        _eye.Fill = _kind == PetKind.Robot ? Solid("#2DD4FF") : Solid("#2B2B33");
        _eye.RenderTransform = _blink;
        head.Children.Add(Place(_eye, 32.6, 14.4));
        head.Children.Add(Place(new Ellipse { Width = 2.8, Height = 2.1, Fill = _kind == PetKind.Robot ? dark : Solid("#E58A9A") }, 36.2, 18.2));
        _sprite.Children.Add(head);

        // sleepy z and hop heart float above the sprite
        _sprite.Children.Add(Place(_zzz, 34, 0));
        _sprite.Children.Add(Place(_heart, 28, 0));
    }

    private static PointCollection Points(string s) => PointCollection.Parse(s);

    private static UIElement Place(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        return element;
    }

    private void Add(UIElement element, double x, double y) => _sprite.Children.Add(Place(element, x, y));

    private UIElement MakeBody(Brush body, Brush belly)
    {
        var g = new Canvas();
        if (_kind == PetKind.Robot)
        {
            g.Children.Add(Place(new Rectangle { Width = 24, Height = 13, RadiusX = 4, RadiusY = 4, Fill = body, Stroke = Solid("#6B7F94"), StrokeThickness = 0.8 }, 6, 15));
            g.Children.Add(Place(new Rectangle { Width = 9, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Fill = Solid("#2DD4FF"), Opacity = 0.85 }, 12, 19));
        }
        else
        {
            g.Children.Add(Place(new Ellipse { Width = 25, Height = 13.5, Fill = body }, 6, 15));
            g.Children.Add(Place(new Ellipse { Width = 14, Height = 6, Fill = belly, Opacity = 0.85 }, 11, 21.5));
        }
        return g;
    }

    private void AddLeg(Brush fill, double x, double phase)
    {
        var rot = new RotateTransform(0);
        var leg = new Rectangle { Width = 4, Height = 9, RadiusX = 2, RadiusY = 2, Fill = fill, RenderTransformOrigin = new Point(0.5, 0.1), RenderTransform = rot };
        Add(leg, x, SpriteHeight - 12);
        _legs.Add((leg, rot, phase));
    }

    // ------------------------------------------------------------------ behaviour

    private void SetMood(Mood mood)
    {
        _mood = mood;
        _moodLeft = mood switch
        {
            Mood.Walk => 3 + _rng.NextDouble() * 5,
            Mood.Idle => 2 + _rng.NextDouble() * 4,
            Mood.Sleep => 8 + _rng.NextDouble() * 10,
            _ => 99,
        };
        if (mood == Mood.Walk) _dir = _x > (_minX + _maxX) / 2 ? -1 : (_rng.Next(2) == 0 ? 1 : -1);
        if (mood != Mood.Sleep) _zzz.Opacity = 0;
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.1);
        _last = now;
        _phase += dt;
        _moodLeft -= dt;

        // blink now and then (not while asleep)
        _nextBlink -= dt;
        var eyeScale = 1.0;
        if (_mood == Mood.Sleep) eyeScale = 0.12;
        else if (_nextBlink < 0.12) eyeScale = Math.Max(0.1, _nextBlink / 0.12);
        if (_nextBlink < 0) _nextBlink = 2 + _rng.NextDouble() * 3;
        _blink.ScaleY = eyeScale;

        double bobY = 0, squash = 1, nod = 0, wag;
        wag = Math.Sin(_phase * (_mood == Mood.Dance ? 14 : 5)) * (_mood == Mood.Sleep ? 2 : 14);

        switch (_mood)
        {
            case Mood.Walk:
            {
                _x += _dir * 20 * dt;
                if (_x <= _minX) { _x = _minX; _dir = 1; }
                else if (_x >= _maxX) { _x = _maxX; _dir = -1; }
                foreach (var (_, rot, phase) in _legs) rot.Angle = Math.Sin(_phase * 11 + phase) * 30;
                bobY = -Math.Abs(Math.Sin(_phase * 11)) * 1.4;
                nod = Math.Sin(_phase * 5.5) * 2;
                if (_moodLeft <= 0) SetMood(_rng.NextDouble() < 0.5 ? Mood.Idle : Mood.Walk);
                break;
            }
            case Mood.Idle:
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                nod = Math.Sin(_phase * 1.6) * 3;
                if (_moodLeft <= 0) SetMood(_music ? Mood.Dance : _rng.NextDouble() < 0.35 ? Mood.Sleep : Mood.Walk);
                break;
            case Mood.Sleep:
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                squash = 0.86 + Math.Sin(_phase * 1.8) * 0.02; // slow breathing, lying low
                nod = 16;
                var zt = _phase * 0.8 % 1;
                _zzz.Opacity = Math.Sin(zt * Math.PI);
                SetTop(_zzz, 6 - zt * 8);
                SetLeft(_zzz, 33 + zt * 4);
                if (_moodLeft <= 0) SetMood(Mood.Walk);
                break;
            case Mood.Dance:
                bobY = -Math.Abs(Math.Sin(_phase * 7)) * 3.2;
                squash = 1 + Math.Sin(_phase * 14) * 0.05;
                nod = Math.Sin(_phase * 7) * 10;
                foreach (var (_, rot, phase) in _legs) rot.Angle = Math.Sin(_phase * 14 + phase) * 16;
                if (!_music) SetMood(Mood.Idle);
                break;
            case Mood.Hop:
            {
                _hopT += dt;
                var u = Math.Clamp(_hopT / 0.55, 0, 1);
                bobY = -Math.Sin(u * Math.PI) * 15;
                squash = 1 + Math.Sin(u * Math.PI) * 0.12;
                nod = -8;
                _heart.Opacity = 1 - u;
                SetTop(_heart, 4 - u * 12);
                SetLeft(_heart, 28);
                if (u >= 1) { _heart.Opacity = 0; SetMood(_music ? Mood.Dance : Mood.Idle); }
                break;
            }
        }

        _tailWag.Angle = wag;
        _headNod.Angle = nod;
        _squash.ScaleY = squash;
        _bob.Y = bobY;
        _flip.ScaleX = _dir;
        _move.X = _x;
    }
}
