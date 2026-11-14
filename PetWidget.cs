// D-Island (personal fork) - a little desktop pet that wanders beneath the island: walks, sits, sleeps, dances to the
// music and hops when you click it. Drawn with vector shapes (four species), animated by a light timer that only
// runs while the pet is on screen.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DIsland;

public enum PetKind { Cat, Dog, Bunny, Robot, Fox, Panda, Dragon, Ghost, Axolotl, Wolf }

public enum PetOutfit { None, Hat, Bow, Glasses, Scarf }

/// <summary>Things happening on the PC that the pet notices.</summary>
public enum PetEvent { Notification, Copy, TimerDone, MusicStart, LowBattery, FocusStart, FocusEnd, Device, Click, Shake, RamHigh, RamFreed }

public sealed class PetWidget : Canvas
{
    public const double SpriteWidth = 44;
    public const double SpriteHeight = 34;

    private enum Mood { Walk, Idle, Sleep, Dance, Hop, Watch, Yawn, Excited, Worried, Study, Follow }

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
    private bool _studying;
    private bool _talks = true;
    private double _lookX, _lookY;            // cursor relative to the pet, in px
    private bool _cursorNear;
    private double _cursorAway;               // seconds since the cursor was last close
    private double _excitedT;
    private double _speechLeft;
    private double _quipIn = 25;
    private readonly Ellipse _mouth = new() { Width = 3.2, Height = 2.2, Fill = new SolidColorBrush(Color.FromRgb(0xB0, 0x55, 0x65)), Opacity = 0 };
    private readonly Rectangle _book = new() { Width = 9, Height = 6, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(Color.FromRgb(0x5E, 0x8B, 0xFF)), Opacity = 0 };
    private readonly TranslateTransform _pupil = new();
    private readonly Border _bubble = new()
    {
        CornerRadius = new CornerRadius(9),
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        Padding = new Thickness(7, 3, 7, 3),
        Opacity = 0,
        IsHitTestVisible = false,
        Child = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)), TextWrapping = TextWrapping.NoWrap },
    };

    public PetWidget()
    {
        Width = SpriteWidth;
        Height = SpriteHeight;
        Children.Add(_sprite);
        Children.Add(_bubble);
        Panel.SetZIndex(_bubble, 5);
        Cursor = System.Windows.Input.Cursors.Hand;
        Background = Brushes.Transparent; // the whole sprite box is clickable
        _sprite.RenderTransform = new TransformGroup { Children = { _squash, _flip, _bob } };
        RenderTransform = _move;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; React(PetEvent.Click); };
        _timer.Tick += (_, _) => Tick();
        Build();
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var dark = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2A));
            var menu = new ContextMenu { Background = dark, Foreground = Brushes.White, Padding = new Thickness(4) };
            MenuItem Item(string text, Action a) { var m = new MenuItem { Header = text, Background = dark, Foreground = Brushes.White }; m.Click += (_, _) => a(); return m; }
            menu.Items.Add(Item("Feed a treat", Feed));
            menu.Items.Add(Item("Pet", () => React(PetEvent.Click)));
            menu.Items.Add(Item(_mood == Mood.Sleep ? "Wake up" : "Take a nap", () => SetMood(_mood == Mood.Sleep ? Mood.Idle : Mood.Yawn)));
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        };
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

    public PetOutfit Outfit
    {
        get => _outfit;
        set { if (value == _outfit) return; _outfit = value; Build(); }
    }
    private PetOutfit _outfit = PetOutfit.None;

    /// <summary>Quiet mode: the pet curls up and says nothing.</summary>
    public bool Quiet
    {
        get => _quiet;
        set
        {
            if (value == _quiet) return;
            _quiet = value;
            if (value) { _bubble.Opacity = 0; SetMood(Mood.Sleep); _moodLeft = 9999; }
            else SetMood(Mood.Idle);
        }
    }
    private bool _quiet;

    // ---- hunger and care
    private double _hunger;

    /// <summary>Feed the pet a treat.</summary>
    public void Feed()
    {
        _hunger = 0;
        _excitedT = 0;
        SetMood(Mood.Excited);
        _heart.Opacity = 1;
        Say(Pick("yum!", "nom nom", "my favourite", "thank you!"));
    }

    public bool Talks { get => _talks; set => _talks = value; }

    /// <summary>Studying (a focus session is running): the pet sits quietly with a little book.</summary>
    public bool Studying
    {
        get => _studying;
        set
        {
            if (_studying == value) return;
            _studying = value;
            if (value) SetMood(Mood.Study);
            else if (_mood == Mood.Study) SetMood(Mood.Idle);
        }
    }

    /// <summary>Where the cursor is, relative to the pet's top-left corner (px), and whether it is close by.</summary>
    public void Observe(double dx, double dy, bool near)
    {
        _lookX = dx;
        _lookY = dy;
        _cursorNear = near;
        if (near) _cursorAway = 0;
    }

    public void React(PetEvent evt, string? detail = null)
    {
        if (_quiet && evt != PetEvent.Click) return;
        switch (evt)
        {
            case PetEvent.Notification:
                Excite();
                Say(detail is { Length: > 0 } ? Pick($"{detail} wrote something!", "ooh, a message!", "you've got mail!") : Pick("ooh, a message!", "ding!"));
                break;
            case PetEvent.Copy:
                Say(Pick("copied!", "ctrl+c, nice", "got it"));
                break;
            case PetEvent.TimerDone:
                Excite();
                Say(Pick("time's up!", "ding ding!", "that's the timer!"));
                break;
            case PetEvent.MusicStart:
                Say(Pick("nice song", "ooh, music!", "play it loud"));
                break;
            case PetEvent.LowBattery:
                SetMood(Mood.Worried);
                Say(Pick("battery's low...", "need power!", "please plug in"));
                break;
            case PetEvent.FocusStart:
                Say(Pick("focus time. I'll be quiet.", "let's do this", "I'll keep watch"));
                break;
            case PetEvent.FocusEnd:
                Excite();
                Say(Pick("break time!", "you did it!", "stretch your legs"));
                break;
            case PetEvent.Device:
                Say(Pick("something plugged in", "new friend?", "oh, a device"));
                break;
            case PetEvent.RamHigh:
                SetMood(Mood.Worried);
                Say(Pick("my head feels so full...", "too many tabs!", "RAM is almost gone!", "can we close something?"), 3.6);
                break;
            case PetEvent.RamFreed:
                Excite();
                Say(detail is { Length: > 0 } ? Pick($"{detail} freed, I feel light!", $"phew, {detail} back!") : "so much lighter!", 3.2);
                break;
            case PetEvent.Shake:
                Say(Pick("whoa!", "hey, easy!"));
                break;
            case PetEvent.Click:
                if (_mood == Mood.Sleep) { Say(Pick("mrrp?", "hm? five more minutes", "I was dreaming")); SetMood(Mood.Idle); }
                else { Hop(); Say(Pick(ClickLines)); }
                break;
        }
    }

    private string[] ClickLines => _kind switch
    {
        PetKind.Dragon => new[] { "*tiny puff of smoke*", "behold, a dragon", "I could burn your to-do list", "hoard acquired" },
        PetKind.Ghost => new[] { "boo! ...sorry", "you can't hurt me, I checked", "ooOOoo", "that tickled my ectoplasm" },
        PetKind.Axolotl => new[] { "blub", "I regenerate, you know", "smiling is my default", "wiggle wiggle" },
        PetKind.Wolf => new[] { "...fine. one pat.", "I'm not a dog.", "awoo?", "pack mentality" },
        PetKind.Robot => new[] { "beep boop, affection logged", "bzzt. that was nice", "emotion.exe running" },
        PetKind.Fox => new[] { "heh, sly one", "what does the fox say?", "tickles!" },
        PetKind.Bunny => new[] { "hop hop!", "nose wiggles", "again!" },
        PetKind.Panda => new[] { "bamboo break?", "rolling over...", "hehe" },
        _ => new[] { "hehe", "purr", "again!", "that tickles" },
    };

    private string[] IdleLines => _kind switch
    {
        PetKind.Dragon => new[] { "I guard this pill.", "any treasure around?", "knights are so last century", "hm. snack?" },
        PetKind.Ghost => new[] { "I live here rent-free", "spooky, huh?", "did you feel a draft?", "...boo." },
        PetKind.Axolotl => new[] { "just vibing, underwater", "I can regrow a leg", "tiny fish thoughts", "blub." },
        PetKind.Wolf => new[] { "the pack is quiet tonight", "I'm watching.", "I don't do cute.", "..." },
        PetKind.Robot => new[] { "systems nominal", "defragmenting feelings", "running idle.exe", "..." },
        PetKind.Fox => new[] { "what are we scheming?", "I smell a plan", "cunning mode: on" },
        _ => new[] { "hm.", "what are we up to?", "nice day for it", "...", "I'm hungry", "don't mind me" },
    };

    private string Pick(params string[] options) => options[_rng.Next(options.Length)];

    private void Excite()
    {
        _excitedT = 0;
        SetMood(Mood.Excited);
    }

    /// <summary>A little speech bubble beside the pet.</summary>
    private void Say(string text, double seconds = 2.8)
    {
        if (!_talks || _quiet) return;
        ((TextBlock)_bubble.Child).Text = text;
        _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var right = _x < (_minX + _maxX) / 2; // keep it on the side with room
        SetLeft(_bubble, right ? SpriteWidth + 2 : -_bubble.DesiredSize.Width - 2);
        SetTop(_bubble, 4);
        _bubble.Opacity = 1;
        _speechLeft = seconds;
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
        // _eye and _mouth live inside the old head canvas; detach them or the rebuild throws
        // ("already a child of another visual") and leaves the pet headless and frozen.
        foreach (var shared in new FrameworkElement[] { _eye, _mouth })
            if (VisualTreeHelper.GetParent(shared) is Canvas oldParent) oldParent.Children.Remove(shared);
        _sprite.Children.Clear();
        _legs.Clear();
        var (body, dark, belly, earInner) = _kind switch
        {
            PetKind.Dog => (Solid("#D9A86C"), Solid("#8B5A2B"), Solid("#F0D5A8"), Solid("#8B5A2B")),
            PetKind.Bunny => (Solid("#F4F4F8"), Solid("#D8D8E2"), Solid("#FFFFFF"), Solid("#FFB3C7")),
            PetKind.Robot => (Solid("#9FB3C8"), Solid("#6B7F94"), Solid("#C9D6E3"), Solid("#2DD4FF")),
            PetKind.Fox => (Solid("#E8803A"), Solid("#2B2B33"), Solid("#FFF2E2"), Solid("#2B2B33")),
            PetKind.Panda => (Solid("#F6F6F9"), Solid("#2A2A32"), Solid("#FFFFFF"), Solid("#2A2A32")),
            PetKind.Dragon => (Solid("#4FD18B"), Solid("#2C8C5E"), Solid("#F4F0A8"), Solid("#FFB347")),
            PetKind.Ghost => (Solid("#EDF0FF"), Solid("#AEB6E8"), Solid("#FFFFFF"), Solid("#AEB6E8")),
            PetKind.Axolotl => (Solid("#FFB8CC"), Solid("#E8789C"), Solid("#FFE6EE"), Solid("#FF5C93")),
            PetKind.Wolf => (Solid("#8C95A8"), Solid("#4B5266"), Solid("#DCE0EA"), Solid("#4B5266")),
            _ => (Solid("#E9B27A"), Solid("#C98A52"), Solid("#F7DDBE"), Solid("#FFB3C7")),
        };

        _sprite.Opacity = _kind == PetKind.Ghost ? 0.82 : 1;
        Add(new Ellipse { Width = 26, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) }, 9, SpriteHeight - 4);

        // tail (behind the body)
        var tail = new Canvas { RenderTransform = _tailWag };
        switch (_kind)
        {
            case PetKind.Cat:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,21 C 0,20 -2,12 3,9"), Stroke = body, StrokeThickness = 3.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case PetKind.Fox:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,22 C -1,22 -3,13 2,9"), Stroke = body, StrokeThickness = 6.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                tail.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = belly, Margin = new Thickness(-0.5, 6, 0, 0) });
                break;
            case PetKind.Panda:
                tail.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = body, Stroke = dark, StrokeThickness = 0.6, Margin = new Thickness(1, 17, 0, 0) });
                break;
            case PetKind.Dog:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,21 L 3,14"), Stroke = body, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case PetKind.Bunny:
                tail.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = belly, Margin = new Thickness(1, 17, 0, 0) });
                break;
            case PetKind.Dragon:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,22 C 1,23 -1,16 3,12"), Stroke = body, StrokeThickness = 4.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                tail.Children.Add(new Polygon { Points = Points("0,0 6,2 1,6"), Fill = earInner, Margin = new Thickness(-2, 8, 0, 0) });
                break;
            case PetKind.Ghost:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 9,24 C 5,20 3,27 -1,22"), Stroke = body, StrokeThickness = 3.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case PetKind.Axolotl:
                tail.Children.Add(new Ellipse { Width = 12, Height = 6, Fill = dark, Opacity = 0.9, Margin = new Thickness(-4, 17, 0, 0) });
                break;
            case PetKind.Wolf:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,22 C 0,23 -2,15 3,11"), Stroke = body, StrokeThickness = 5.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                tail.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = dark, Margin = new Thickness(0.5, 8.5, 0, 0) });
                break;
            default:
                tail.Children.Add(new Path { Data = Geometry.Parse("M 8,23 L 3,23"), Stroke = dark, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round });
                break;
        }
        _sprite.Children.Add(tail);

        // legs: far pair slightly darker, then the body over them
        AddLeg(dark, 10, 0);
        AddLeg(dark, 24, Math.PI);
        if (_kind == PetKind.Dragon)
            _sprite.Children.Add(Place(new Polygon { Points = Points("0,7 4,0 7,5 10,0 13,8 6,9"), Fill = dark, Opacity = 0.95 }, 11, 8));
        _sprite.Children.Add(MakeBody(body, belly));
        var near = _kind == PetKind.Panda ? dark : body;
        AddLeg(near, 14, Math.PI);
        AddLeg(near, 29, 0);
        if (_kind == PetKind.Ghost)
            foreach (var (leg, _, _) in _legs) leg.Fill = Brushes.Transparent; // a ghost floats

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
                case PetKind.Panda:
                    head.Children.Add(Place(new Ellipse { Width = 5.4, Height = 5.4, Fill = dark }, 23, 7.6));
                    head.Children.Add(Place(new Ellipse { Width = 5.4, Height = 5.4, Fill = dark }, 33, 7.6));
                    head.Children.Add(Place(new Ellipse { Width = 5.2, Height = 6, Fill = dark, RenderTransform = new RotateTransform(-18, 2.6, 3) }, 30.4, 12.4)); // eye patch
                    break;
                case PetKind.Cat:
                case PetKind.Fox:
                    head.Children.Add(Place(new Polygon { Points = Points("0,7 1.2,0 5,4.6"), Fill = _kind == PetKind.Fox ? dark : body }, 23.5, 7));
                    head.Children.Add(Place(new Polygon { Points = Points("0,4.6 3.8,0 5,7"), Fill = _kind == PetKind.Fox ? dark : body }, 31, 7));
                    head.Children.Add(Place(new Polygon { Points = Points("1,5.4 1.8,2 3.6,4.2"), Fill = earInner }, 24, 8.4));
                    head.Children.Add(Place(new Polygon { Points = Points("0.4,4.2 2.2,2 3,5.4"), Fill = earInner }, 32, 8.4));
                    break;
                case PetKind.Dog:
                    head.Children.Add(Place(new Ellipse { Width = 5.5, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(14, 2.7, 1) }, 20.6, 10));
                    head.Children.Add(Place(new Ellipse { Width = 5.5, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(-14, 2.7, 1) }, 33.6, 10.5));
                    break;
                case PetKind.Dragon:
                    head.Children.Add(Place(new Polygon { Points = Points("0,7 1.6,0 4.6,5"), Fill = earInner }, 23.4, 6));
                    head.Children.Add(Place(new Polygon { Points = Points("0,5 3,0 4.6,7"), Fill = earInner }, 31.6, 6));
                    head.Children.Add(Place(new Ellipse { Width = 5, Height = 3.4, Fill = dark, Opacity = 0.5 }, 33, 19.2)); // snout shade
                    break;
                case PetKind.Wolf:
                    head.Children.Add(Place(new Polygon { Points = Points("0,9 1,0 6,5.4"), Fill = dark }, 22.6, 5.5));
                    head.Children.Add(Place(new Polygon { Points = Points("0,5.4 5,0 6,9"), Fill = dark }, 30.4, 5.5));
                    head.Children.Add(Place(new Polygon { Points = Points("0,6 1,2 3.2,4.6"), Fill = body }, 24, 8));
                    head.Children.Add(Place(new Polygon { Points = Points("0,4.6 2.2,2 3.2,6"), Fill = body }, 32, 8));
                    head.Children.Add(Place(new Ellipse { Width = 7, Height = 5, Fill = belly, Opacity = 0.9 }, 31.6, 18)); // pale muzzle
                    break;
                case PetKind.Axolotl:
                    foreach (var (gx, gy, gr) in new[] { (22.0, 12.0, -50.0), (21.0, 15.5, -80.0), (22.5, 18.5, -115.0) })
                        head.Children.Add(Place(new Ellipse { Width = 6.2, Height = 2.2, Fill = earInner, RenderTransform = new RotateTransform(gr, 3.1, 1.1) }, gx - 4, gy));
                    foreach (var (gx, gy, gr) in new[] { (35.5, 10.5, 40.0), (36.5, 14.0, 70.0) })
                        head.Children.Add(Place(new Ellipse { Width = 6.2, Height = 2.2, Fill = earInner, RenderTransform = new RotateTransform(gr, 3.1, 1.1) }, gx, gy));
                    break;
                case PetKind.Ghost:
                    head.Children.Add(Place(new Ellipse { Width = 3.2, Height = 2, Fill = Solid("#FFB8D0"), Opacity = 0.7 }, 28, 18.4)); // blush
                    break;
                case PetKind.Bunny:
                    head.Children.Add(Place(new Ellipse { Width = 4.6, Height = 15, Fill = body, Stroke = dark, StrokeThickness = 0.6, RenderTransform = new RotateTransform(-8, 2.3, 14) }, 24.5, -4));
                    head.Children.Add(Place(new Ellipse { Width = 4.6, Height = 15, Fill = body, Stroke = dark, StrokeThickness = 0.6, RenderTransform = new RotateTransform(10, 2.3, 14) }, 31, -3.5));
                    head.Children.Add(Place(new Ellipse { Width = 2, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(-8, 1, 10) }, 25.8, -1.5));
                    head.Children.Add(Place(new Ellipse { Width = 2, Height = 10, Fill = earInner, RenderTransform = new RotateTransform(10, 1, 10) }, 32.3, -1));
                    break;
            }
        }
        _eye.Fill = _kind == PetKind.Robot ? Solid("#2DD4FF") : _kind == PetKind.Panda ? Solid("#FFFFFF") : Solid("#2B2B33");
        _eye.RenderTransform = new TransformGroup { Children = { _blink, _pupil } };
        head.Children.Add(Place(_eye, 32.6, 14.4));
        head.Children.Add(Place(new Ellipse { Width = 2.8, Height = 2.1, Fill = _kind == PetKind.Robot ? dark : Solid("#E58A9A") }, 36.2, 18.2));
        head.Children.Add(Place(_mouth, 35.6, 20.4));
        switch (Outfit)
        {
            case PetOutfit.Hat:
                head.Children.Add(Place(new Polygon { Points = Points("0,9 5,0 10,9"), Fill = Solid("#7C5CFF"), Stroke = Solid("#C9B8FF"), StrokeThickness = 0.6 }, 25.5, -1));
                head.Children.Add(Place(new Ellipse { Width = 3.4, Height = 3.4, Fill = Solid("#FFD66B") }, 28.8, -3));
                break;
            case PetOutfit.Bow:
                head.Children.Add(Place(new Polygon { Points = Points("0,0 6,3 0,6"), Fill = Solid("#FF6B9A") }, 22, 6.5));
                head.Children.Add(Place(new Polygon { Points = Points("12,0 6,3 12,6"), Fill = Solid("#FF6B9A") }, 22, 6.5));
                head.Children.Add(Place(new Ellipse { Width = 3.2, Height = 3.2, Fill = Solid("#FF3F7F") }, 26.4, 8));
                break;
            case PetOutfit.Glasses:
                head.Children.Add(Place(new Ellipse { Width = 6.4, Height = 6.4, Stroke = Solid("#20202A"), StrokeThickness = 1.1, Fill = new SolidColorBrush(Color.FromArgb(0x30, 120, 200, 255)) }, 29.6, 11.6));
                head.Children.Add(Place(new Ellipse { Width = 5.2, Height = 5.2, Stroke = Solid("#20202A"), StrokeThickness = 1.1, Fill = new SolidColorBrush(Color.FromArgb(0x30, 120, 200, 255)) }, 23.6, 11.8));
                head.Children.Add(Place(new Line { X1 = 28.8, Y1 = 14.5, X2 = 29.8, Y2 = 14.5, Stroke = Solid("#20202A"), StrokeThickness = 1 }, 0, 0));
                break;
            case PetOutfit.Scarf:
                head.Children.Add(Place(new Rectangle { Width = 12, Height = 4.2, RadiusX = 2, RadiusY = 2, Fill = Solid("#E8504A") }, 23.2, 20.6));
                head.Children.Add(Place(new Rectangle { Width = 3.4, Height = 6, RadiusX = 1.2, RadiusY = 1.2, Fill = Solid("#C93A34") }, 24, 22.4));
                break;
        }
        _sprite.Children.Add(head);
        _sprite.Children.Add(Place(_book, 29, SpriteHeight - 9));

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
            Mood.Dance => 5,
            Mood.Yawn => 1.4,
            Mood.Watch => 3 + _rng.NextDouble() * 4,
            Mood.Follow => 6,
            Mood.Excited => 1.6,
            Mood.Worried => 3.2,
            _ => 99,
        };
        if (mood == Mood.Walk) _dir = _x > (_minX + _maxX) / 2 ? -1 : (_rng.Next(2) == 0 ? 1 : -1);
        if (mood != Mood.Sleep) _zzz.Opacity = 0;
        if (mood != Mood.Study) _book.Opacity = 0;
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.1);
        _last = now;
        _phase += dt;
        _moodLeft -= dt;
        if (!_cursorNear) _cursorAway += dt;
        if (_quiet && _mood != Mood.Sleep) SetMood(Mood.Sleep);
        if (_quiet) _moodLeft = 9999;

        // speech bubble fades out; now and then an idle remark
        if (_speechLeft > 0)
        {
            _speechLeft -= dt;
            if (_speechLeft < 0.4) _bubble.Opacity = Math.Max(0, _speechLeft / 0.4);
        }
        _hunger = Math.Min(1, _hunger + dt / 480);   // hungry after about eight minutes
        if (_hunger > 0.9 && _speechLeft <= 0 && !_quiet && _mood is Mood.Idle or Mood.Walk or Mood.Watch && _rng.NextDouble() < dt / 40) Say(Pick("I'm hungry...", "is it snack time?", "my tummy is rumbling"), 3);
        _quipIn -= dt;
        if (_quipIn <= 0)
        {
            _quipIn = 40 + _rng.NextDouble() * 50;
            if (_speechLeft <= 0 && _mood is Mood.Idle or Mood.Walk or Mood.Watch)
                Say(Pick(IdleLines), 2.4);
        }

        // blink now and then (not while asleep)
        _nextBlink -= dt;
        var eyeScale = 1.0;
        if (_mood == Mood.Sleep) eyeScale = 0.12;
        else if (_mood == Mood.Study) eyeScale = 0.45; // eyes down on the book
        else if (_nextBlink < 0.12) eyeScale = Math.Max(0.1, _nextBlink / 0.12);
        if (_nextBlink < 0) _nextBlink = 2 + _rng.NextDouble() * 3;
        _blink.ScaleY = eyeScale;

        // watch the cursor with the eyes whenever it is near
        if (_cursorNear && _mood != Mood.Sleep)
        {
            var len = Math.Max(1, Math.Sqrt(_lookX * _lookX + _lookY * _lookY));
            _pupil.X = Math.Clamp(_lookX / len * 1.3, -1.3, 1.3) * _dir;
            _pupil.Y = Math.Clamp(_lookY / len * 1.1, -1.1, 1.1);
        }
        else { _pupil.X *= 0.8; _pupil.Y *= 0.8; }

        double bobY = 0, squash = 1, nod = 0, wag = Math.Sin(_phase * 5) * 14, shake = 0, mouth = 0;
        _book.Opacity = _mood == Mood.Study ? 1 : 0;

        // the cursor arriving close by interrupts what the pet is doing
        if (_cursorNear && _mood is Mood.Walk or Mood.Idle)
        {
            SetMood(Mood.Watch);
            if (_rng.NextDouble() < 0.4) Say(Pick("hi!", "I see you", "what's that?"), 1.8);
        }
        else if (_cursorNear && _mood == Mood.Sleep && Math.Abs(_lookX) < 60) { SetMood(Mood.Idle); Say(Pick("hm?", "mrrp?"), 1.6); }

        switch (_mood)
        {
            case Mood.Walk:
            case Mood.Follow:
            {
                var speed = _mood == Mood.Follow ? 38 : 20;
                if (_mood == Mood.Follow) _dir = _lookX >= 0 ? 1 : -1;
                _x += _dir * speed * dt;
                if (_x <= _minX) { _x = _minX; _dir = 1; }
                else if (_x >= _maxX) { _x = _maxX; _dir = -1; }
                foreach (var (_, rot, phase) in _legs) rot.Angle = Math.Sin(_phase * (_mood == Mood.Follow ? 16 : 11) + phase) * 30;
                bobY = -Math.Abs(Math.Sin(_phase * 11)) * 1.4;
                nod = Math.Sin(_phase * 5.5) * 2;
                if (_mood == Mood.Follow && (Math.Abs(_lookX) < 34 || _moodLeft <= 0)) SetMood(Mood.Watch);
                else if (_mood == Mood.Walk && _moodLeft <= 0) SetMood(_rng.NextDouble() < 0.5 ? Mood.Idle : Mood.Walk);
                break;
            }
            case Mood.Idle:
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                nod = Math.Sin(_phase * 1.6) * 3;
                if (_moodLeft <= 0)
                {
                    var night = DateTime.Now.Hour is < 6 or >= 23;
                    SetMood(_music ? Mood.Dance : _rng.NextDouble() < (night ? 0.7 : 0.3) ? Mood.Yawn : Mood.Walk);
                }
                break;
            case Mood.Watch:
            {
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                squash = 0.94;
                // face the cursor, tilt the head up or down toward it, and follow it if it lingers
                if (Math.Abs(_lookX) > 8) _dir = _lookX >= 0 ? 1 : -1;
                nod = Math.Clamp(_lookY / 20, -10, 10) * -0.8 + Math.Sin(_phase * 2.4) * 2;
                if (_cursorNear && Math.Abs(_lookX) > 90 && _moodLeft < 1.5) SetMood(Mood.Follow);
                else if (_cursorAway > 2.5 || _moodLeft <= 0 && !_cursorNear) SetMood(_music ? Mood.Dance : Mood.Idle);
                else if (_moodLeft <= 0) _moodLeft = 3;
                break;
            }
            case Mood.Yawn:
            {
                var u = Math.Clamp(1 - _moodLeft / 1.4, 0, 1);
                nod = -Math.Sin(u * Math.PI) * 14;
                mouth = Math.Sin(u * Math.PI);
                squash = 1 + Math.Sin(u * Math.PI) * 0.05;
                _blink.ScaleY = Math.Max(0.1, 1 - Math.Sin(u * Math.PI));
                if (_moodLeft <= 0) SetMood(Mood.Sleep);
                break;
            }
            case Mood.Sleep:
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                squash = 0.86 + Math.Sin(_phase * 1.8) * 0.02; // slow breathing, lying low
                nod = 16;
                wag = Math.Sin(_phase * 1.2) * 2;
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
                wag = Math.Sin(_phase * 14) * 20;
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8; // feet planted: it bounces and bobs, it doesn't march
                if (!_music) SetMood(Mood.Idle);
                else if (_moodLeft <= 0) SetMood(Mood.Walk); // dance a few seconds, then wander on
                break;
            case Mood.Excited:
            {
                _excitedT += dt;
                bobY = -Math.Abs(Math.Sin(_excitedT * 9)) * 6;
                squash = 1 + Math.Abs(Math.Sin(_excitedT * 9)) * 0.08;
                nod = -10;
                wag = Math.Sin(_phase * 24) * 24;
                if (_moodLeft <= 0) SetMood(_cursorNear ? Mood.Watch : Mood.Idle);
                break;
            }
            case Mood.Worried:
                shake = Math.Sin(_phase * 38) * 0.7;
                nod = 6;
                squash = 0.96;
                wag = 0;
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                if (_moodLeft <= 0) SetMood(Mood.Idle);
                break;
            case Mood.Study:
                foreach (var (_, rot, _) in _legs) rot.Angle *= 0.8;
                squash = 0.93;
                nod = 14 + Math.Sin(_phase * 1.2) * 1.5; // head down over the book
                wag = Math.Sin(_phase * 1.1) * 3;
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
                if (u >= 1) { _heart.Opacity = 0; SetMood(_studying ? Mood.Study : _music ? Mood.Dance : Mood.Idle); }
                break;
            }
        }

        // while it speaks, the mouth moves a little
        if (_speechLeft > 0.5 && mouth == 0) mouth = (Math.Sin(_phase * 18) + 1) * 0.35;
        _mouth.Opacity = mouth > 0.05 ? 1 : 0;
        _mouth.RenderTransform = new ScaleTransform(1, Math.Max(0.2, mouth * 1.4), 1.6, 0);

        _tailWag.Angle = wag;
        _headNod.Angle = nod;
        _squash.ScaleY = squash;
        _bob.Y = bobY;
        _flip.ScaleX = _dir;
        _move.X = _x + shake;
    }
}
