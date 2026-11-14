// D-Island (personal fork) - Focus sessions (Pomodoro): a ring timer page that drives the island's shared timer.

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

public enum FocusPhase { Idle, Focus, ShortBreak, LongBreak }

/// <summary>The Pomodoro cycle: focus, short break, focus... and a long break after a few rounds.</summary>
public sealed class FocusSession
{
    private static readonly string StatsFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "focus.json");

    private sealed class Stats { public string Day { get; set; } = ""; public int Sessions { get; set; } public int Minutes { get; set; } }

    private readonly AppSettings _settings;
    private readonly TimerService _timer;
    private Stats _stats = new();

    public FocusSession(AppSettings settings, TimerService timer)
    {
        _settings = settings;
        _timer = timer;
        try
        {
            if (File.Exists(StatsFile)) _stats = JsonSerializer.Deserialize<Stats>(File.ReadAllText(StatsFile)) ?? new();
        }
        catch (Exception) { }
    }

    public FocusPhase Phase { get; private set; }
    public TimeSpan PhaseLength { get; private set; }
    /// <summary>Focus rounds finished in the current cycle (0..CyclesBeforeLong).</summary>
    public int RoundInCycle { get; private set; }
    public int TodaySessions => _stats.Day == Today ? _stats.Sessions : 0;
    public int TodayMinutes => _stats.Day == Today ? _stats.Minutes : 0;
    public bool Running => Phase != FocusPhase.Idle && _timer.IsActive;
    public bool Paused => Phase != FocusPhase.Idle && _timer.IsPaused;
    public TimeSpan Remaining => _timer.Remaining;

    public event Action? Changed;

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public void Start(FocusPhase phase)
    {
        Phase = phase;
        PhaseLength = TimeSpan.FromMinutes(phase switch
        {
            FocusPhase.Focus => Math.Max(1, _settings.FocusMinutes),
            FocusPhase.ShortBreak => Math.Max(1, _settings.ShortBreakMinutes),
            _ => Math.Max(1, _settings.LongBreakMinutes),
        });
        _timer.Start(PhaseLength);
        Changed?.Invoke();
    }

    public void StartFocusWithMinutes(int minutes)
    {
        _settings.FocusMinutes = minutes;
        _settings.NotifyChanged();
        _settings.Save();
        Start(FocusPhase.Focus);
    }

    public void TogglePause()
    {
        if (Phase == FocusPhase.Idle) Start(FocusPhase.Focus);
        else _timer.TogglePause();
        Changed?.Invoke();
    }

    /// <summary>Jump to the next phase now.</summary>
    public void Skip()
    {
        if (Phase == FocusPhase.Idle) { Start(FocusPhase.Focus); return; }
        _timer.Cancel();
        Advance(countAsDone: false);
    }

    public void Stop()
    {
        _timer.Cancel();
        Phase = FocusPhase.Idle;
        Changed?.Invoke();
    }

    /// <summary>The island's timer finished. Returns the message to show, or null if it wasn't a focus phase.</summary>
    public string? OnTimerFinished()
    {
        if (Phase == FocusPhase.Idle) return null;
        var wasFocus = Phase == FocusPhase.Focus;
        Advance(countAsDone: true);
        return wasFocus
            ? (Phase == FocusPhase.LongBreak ? "Focus done. Long break time." : "Focus done. Take a break.")
            : "Break over. Back to it.";
    }

    private void Advance(bool countAsDone)
    {
        var was = Phase;
        if (was == FocusPhase.Focus)
        {
            if (countAsDone)
            {
                if (_stats.Day != Today) _stats = new Stats { Day = Today };
                _stats.Sessions++;
                _stats.Minutes += (int)PhaseLength.TotalMinutes;
                SaveStats();
                RoundInCycle++;
            }
            var long_ = RoundInCycle >= Math.Max(1, _settings.FocusCyclesBeforeLong);
            if (long_) RoundInCycle = 0;
            Phase = long_ ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
        }
        else
        {
            Phase = FocusPhase.Focus;
        }
        if (_settings.FocusAutoStart || !countAsDone) Start(Phase);
        else
        {
            PhaseLength = TimeSpan.FromMinutes(Phase == FocusPhase.Focus ? _settings.FocusMinutes : Phase == FocusPhase.ShortBreak ? _settings.ShortBreakMinutes : _settings.LongBreakMinutes);
            Changed?.Invoke();
        }
    }

    private void SaveStats()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StatsFile)!);
            File.WriteAllText(StatsFile, JsonSerializer.Serialize(_stats));
        }
        catch (Exception) { }
    }
}

public sealed class FocusPanel : Grid
{
    private const double RingSize = 150;
    private const double Stroke = 11;

    private readonly FocusSession _session;
    private readonly AppSettings _settings;
    private readonly TimerService _timer;
    private readonly System.Windows.Shapes.Path _arc = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _time = new() { FontSize = 33, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _phase = new() { FontSize = 11.5, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _main = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _mainGlyph = new() { FontFamily = AssistantPanel.IconFont, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    private readonly Border _mainButton;
    private readonly TextBlock _stats = new() { FontSize = 11.5, Opacity = 0.6 };
    private readonly StackPanel _presets = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

    public FocusPanel(FocusSession session, AppSettings settings, TimerService timer)
    {
        _session = session;
        _settings = settings;
        _timer = timer;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());

        // ---- ring
        var ring = new Grid { Width = RingSize, Height = RingSize, Margin = new Thickness(2, 0, 16, 0) };
        ring.Children.Add(new Ellipse { Stroke = AssistantPanel.B("#26FFFFFF"), StrokeThickness = Stroke, Margin = new Thickness(Stroke / 2) });
        ring.Children.Add(_arc);
        ring.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _phase, _time, _dots } });
        Children.Add(ring);

        // ---- controls
        _mainButton = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 9, 20, 9),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _mainGlyph, _main } },
        };
        _mainButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; _session.TogglePause(); };
        _mainButton.MouseEnter += (_, _) => AssistantPanel.PopScale(_mainButton, 1.04);
        _mainButton.MouseLeave += (_, _) => AssistantPanel.PopScale(_mainButton, 1);

        var secondary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var skip = AssistantPanel.Chip("", "Skip", () => _session.Skip());
        var reset = AssistantPanel.Chip("", "Reset", () => _session.Stop());
        reset.Margin = new Thickness(6, 0, 0, 0);
        secondary.Children.Add(skip);
        secondary.Children.Add(reset);

        foreach (var minutes in new[] { 15, 25, 45, 50 })
        {
            var chip = AssistantPanel.Chip("", $"{minutes}", () => _session.StartFocusWithMinutes(minutes));
            chip.Margin = new Thickness(0, 0, 5, 0);
            chip.ToolTip = $"Start a {minutes}-minute focus session";
            _presets.Children.Add(chip);
        }

        var side = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Focus", FontSize = 16, FontWeight = FontWeights.SemiBold },
                _stats,
                new Border { Height = 12 },
                _mainButton,
                secondary,
                new TextBlock { Text = "Quick start (minutes)", FontSize = 10.5, Opacity = 0.5, Margin = new Thickness(0, 12, 0, 0) },
                _presets,
            },
        };
        SetColumn(side, 1);
        Children.Add(side);

        _session.Changed += Refresh;
        _timer.Tick += Refresh;
        _settings.Changed += Refresh;
        Refresh();
    }

    private Color PhaseColor => _session.Phase switch
    {
        FocusPhase.ShortBreak or FocusPhase.LongBreak => Color.FromRgb(0x30, 0xD1, 0x58),
        _ => Color.FromRgb(0xFF, 0x9F, 0x0A),
    };

    private void Refresh()
    {
        var phase = _session.Phase;
        var remaining = phase == FocusPhase.Idle ? TimeSpan.FromMinutes(_settings.FocusMinutes) : _session.Remaining;
        var total = phase == FocusPhase.Idle ? remaining : _session.PhaseLength;
        _time.Text = TimerService.Format(remaining);
        _phase.Text = phase switch
        {
            FocusPhase.Focus => _session.Paused ? "FOCUS · PAUSED" : "FOCUS",
            FocusPhase.ShortBreak => "SHORT BREAK",
            FocusPhase.LongBreak => "LONG BREAK",
            _ => "READY",
        };
        _main.Text = phase == FocusPhase.Idle ? "Start focus" : _session.Paused ? "Resume" : _session.Running ? "Pause" : "Start";
        _mainGlyph.Text = phase == FocusPhase.Idle || _session.Paused || !_session.Running ? "" : "";

        var color = PhaseColor;
        _mainButton.Background = new LinearGradientBrush(color, phase is FocusPhase.ShortBreak or FocusPhase.LongBreak ? Color.FromRgb(0x64, 0xE8, 0xA0) : Color.FromRgb(0xFF, 0xC8, 0x6B), 35);
        _arc.Stroke = new LinearGradientBrush(color, phase is FocusPhase.ShortBreak or FocusPhase.LongBreak ? Color.FromRgb(0x64, 0xE8, 0xA0) : Color.FromRgb(0xFF, 0xC8, 0x6B), 45);

        var fraction = total.TotalSeconds <= 0 ? 0 : Math.Clamp(remaining.TotalSeconds / total.TotalSeconds, 0, 1);
        if (phase == FocusPhase.Idle) fraction = 1;
        _arc.Data = ArcGeometry(fraction);

        _dots.Children.Clear();
        var cycle = Math.Max(1, _settings.FocusCyclesBeforeLong);
        for (var i = 0; i < cycle; i++)
        {
            var done = i < _session.RoundInCycle;
            _dots.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(2, 0, 2, 0),
                Fill = done ? new SolidColorBrush(color) : AssistantPanel.B("#40FFFFFF"),
            });
        }
        var mins = _session.TodayMinutes;
        _stats.Text = _session.TodaySessions == 0
            ? "No sessions yet today"
            : $"Today: {_session.TodaySessions} session{(_session.TodaySessions == 1 ? "" : "s")} · {(mins >= 60 ? $"{mins / 60}h {mins % 60}m" : $"{mins}m")}";
    }

    private static Geometry ArcGeometry(double fraction)
    {
        var radius = (RingSize - Stroke) / 2;
        var center = new Point(RingSize / 2, RingSize / 2);
        if (fraction >= 0.999) return new EllipseGeometry(center, radius, radius);
        if (fraction <= 0.002) return Geometry.Empty;
        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }
}
