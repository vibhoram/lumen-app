// Lumen (personal fork) - alarms on the Home page, right under the timer. One compact row:
//   bell  [ 7 : 30  AM ]  Daily  Set      (scroll the wheel over the hour or the minutes; click AM/PM to flip)
// Your alarms sit on a single quiet line below; tap one to remove it.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private int _alarmMinutes = 7 * 60;
    private bool _alarmDaily;
    private TextBlock _aHour = null!, _aMinute = null!, _aAmPm = null!;
    private StackPanel _alarmChips = null!;
    private Border _dailyChip = null!;

    private static string Clock(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("h:mm tt");

    private TextBlock Segment(string tip, Action<int> wheel, double width)
    {
        var t = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Width = width, TextAlignment = TextAlignment.Center, Cursor = Cursors.SizeNS, ToolTip = tip, Tag = "WheelOwner", Background = Brushes.Transparent, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
        t.MouseWheel += (_, e) => { e.Handled = true; wheel(e.Delta > 0 ? 1 : -1); };
        t.PreviewMouseWheel += (_, e) => { e.Handled = true; wheel(e.Delta > 0 ? 1 : -1); };
        // click: top half goes up, bottom half goes down (for mice without a wheel)
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; wheel(e.GetPosition(t).Y < t.ActualHeight / 2 ? 1 : -1); };
        return t;
    }

    private Border Pill(string text, Action click)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var pill = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, Background = (Brush)FindResource("GlassFill"), BorderBrush = (Brush)FindResource("GlassRim"), BorderThickness = new Thickness(1), Child = label };
        pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return pill;
    }

    private void WireAlarmRow()
    {
        _alarmMinutes = (int)SmartTime.DefaultAlarm(_settings, DateTime.Now).TotalMinutes % 1440; // your usual time, else the next half hour

        _aHour = Segment("Scroll (or click top/bottom) to change the hour", d => Nudge(d * 60), 30);
        _aMinute = Segment("Scroll (or click top/bottom) to change the minutes", d => Nudge(d * 5), 30);
        _aAmPm = Segment("Click to switch AM / PM", d => Nudge(720), 38);
        _aAmPm.Cursor = Cursors.Hand;
        _aAmPm.FontSize = 14;
        _aAmPm.VerticalAlignment = VerticalAlignment.Center;

        var time = new StackPanel { Orientation = Orientation.Horizontal };
        time.Children.Add(_aHour);
        time.Children.Add(new TextBlock { Text = ":", FontSize = 20, FontWeight = FontWeights.SemiBold, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        time.Children.Add(_aMinute);
        time.Children.Add(_aAmPm);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(time, 1);
        row.Children.Add(time);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _dailyChip = Pill("Daily", () => { _alarmDaily = !_alarmDaily; UpdateAlarmRow(); });
        actions.Children.Add(_dailyChip);
        actions.Children.Add(Pill("Set alarm", AddAlarmFromRow));
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        _alarmChips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 5, 0, 0) };
        _alarmSuggest = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        AlarmRow.Children.Add(row);
        AlarmRow.Children.Add(_alarmSuggest);
        AlarmRow.Children.Add(_alarmChips);
        _settings.Changed += BuildAlarmChips;
        BuildStopwatchIdle();
        UpdateAlarmRow();
        BuildAlarmChips();
    }

    private void Nudge(int delta)
    {
        _alarmMinutes = ((_alarmMinutes + delta) % 1440 + 1440) % 1440;
        UpdateAlarmRow();
    }

    private void UpdateAlarmRow()
    {
        var t = DateTime.Today.AddMinutes(_alarmMinutes);
        _aHour.Text = t.ToString("%h");
        _aMinute.Text = t.ToString("mm");
        _aAmPm.Text = t.ToString("tt");
        _dailyChip.Background = _alarmDaily
            ? new LinearGradientBrush(Color.FromRgb(0x3B, 0x9D, 0xFF), Color.FromRgb(0x0A, 0x7A, 0xFF), 90)
            : (Brush)FindResource("GlassFill");
    }

    private void AddAlarmFromRow()
    {
        AddAlarm(TimeSpan.FromMinutes(_alarmMinutes), _alarmDaily);
    }

    private void AddAlarm(TimeSpan timeOfDay, bool daily)
    {
        _settings.Alarms.Add(new Alarm { Time = timeOfDay.ToString(@"hh\:mm"), Enabled = true, Daily = daily, Label = "Alarm" });
        SmartTime.RecordAlarm(_settings, timeOfDay);
        _settings.Save();
        _alarms.Reschedule();
        BuildAlarmChips();
    }

    private WrapPanel _alarmSuggest = null!;
    private string? _alarmSuggestKey;
    private string? _timerSuggestKey;

    /// <summary>"Smart" one-tap alarms for right now (sleep cycles at night, soon-ish during the day, your usual times).</summary>
    private void BuildAlarmSuggestions()
    {
        if (_alarmSuggest is null) return;
        var items = SmartTime.AlarmSuggestions(_settings, DateTime.Now);
        var key = string.Join('|', items.Select(i => i.Label + i.Reason)) + _settings.Alarms.Count;
        if (key == _alarmSuggestKey) return;
        _alarmSuggestKey = key;
        _alarmSuggest.Children.Clear();
        foreach (var item in items)
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new Run(item.Label) { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run("  " + item.Reason) { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 255, 255, 255)) });
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = text, Margin = new Thickness(0, 0, 6, 6), ToolTip = "Set this alarm" };
            var captured = item;
            chip.Click += (_, _) => AddAlarm(captured.TimeOfDay, daily: false);
            _alarmSuggest.Children.Add(chip);
        }
        _alarmSuggest.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Timer lengths: the basics plus what you use most and what fits the time of day.</summary>
    private void BuildTimerPresets()
    {
        var items = SmartTime.TimerSuggestions(_settings, DateTime.Now, _focusSession.TodaySessions);
        var key = string.Join('|', items.Select(i => i.Label + i.Reason));
        if (key == _timerSuggestKey) return;
        _timerSuggestKey = key;
        TimerPresetRow.Children.Clear();
        foreach (var item in items)
        {
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = item.Label, Tag = item.Minutes.ToString(), ToolTip = item.Reason.Length > 0 ? item.Reason : null };
            chip.Click += OnTimerPresetClick;
            TimerPresetRow.Children.Add(chip);
        }
    }

    /// <summary>One quiet line: "Alarms  7:30 AM ✕  · 8:00 AM daily ✕". Nothing at all when there are none.</summary>
    private void BuildAlarmChips()
    {
        if (_alarmChips is null) return;
        _alarmChips.Children.Clear();
        var list = _settings.Alarms.OrderBy(a => a.TimeOfDay).Take(4).ToList();
        foreach (var alarm in list)
        {
            var text = Clock((int)alarm.TimeOfDay.TotalMinutes) + (alarm.Daily ? " daily" : "") + "  ✕";
            var chip = new TextBlock { Text = text, FontSize = 11.5, Opacity = alarm.Enabled ? 0.7 : 0.35, Margin = new Thickness(0, 0, 14, 0), Cursor = Cursors.Hand, ToolTip = "Click to remove", Background = Brushes.Transparent };
            chip.MouseEnter += (_, _) => chip.Opacity = 1;
            chip.MouseLeave += (_, _) => chip.Opacity = alarm.Enabled ? 0.7 : 0.35;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _settings.Alarms.Remove(alarm);
                _settings.Save();
                _alarms.Reschedule();
                BuildAlarmChips();
            };
            _alarmChips.Children.Add(chip);
        }
        _alarmChips.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Home) Dispatcher.InvokeAsync(Render);
    }

    // ------------------------------------------------------------------ Alarm / Timer / Stopwatch switcher

    private enum ClockMode { Alarm, Timer, Stopwatch }

    private ClockMode _clockMode = ClockMode.Alarm;
    private System.Windows.Threading.DispatcherTimer? _swCountdown;
    private int _swCount;
    private Border? _swStartPill;
    private TextBlock? _swStartText;

    private FrameworkElement RegionFor(ClockMode mode) => mode switch
    {
        ClockMode.Timer => _timer.IsActive ? TimerActivePanel : TimerPresets,
        ClockMode.Stopwatch => _stopwatch.IsActive ? StopwatchPanel : StopwatchIdlePanel,
        _ => AlarmRow,
    };

    /// <summary>Shows only the options of the current mode (no animation: used on every refresh).</summary>
    private void ApplyClockMode()
    {
        if (_clockMode == ClockMode.Timer) BuildTimerPresets();
        if (_clockMode == ClockMode.Alarm) BuildAlarmSuggestions();
        var shown = RegionFor(_clockMode);
        foreach (var region in new FrameworkElement[] { AlarmRow, TimerPresets, TimerActivePanel, StopwatchPanel, StopwatchIdlePanel })
        {
            if (region == _leavingClockRegion) continue; // still fading out
            region.Visibility = region == shown ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateModeBar();
    }

    private FrameworkElement? _leavingClockRegion;

    private static TranslateTransform ShiftOf(FrameworkElement e)
    {
        if (e.RenderTransform is TransformGroup { Children.Count: 2 } g && g.Children[1] is TranslateTransform t) return t;
        var scale = new ScaleTransform(1, 1);
        var shift = new TranslateTransform();
        e.RenderTransformOrigin = new Point(0.5, 0.2);
        e.RenderTransform = new TransformGroup { Children = { scale, shift } };
        return shift;
    }

    private static ScaleTransform ScaleOf(FrameworkElement e)
    {
        ShiftOf(e);
        return (ScaleTransform)((TransformGroup)e.RenderTransform).Children[0];
    }

    /// <summary>Swaps the options with a fluid hand-over: the old ones fade up and away, the new ones spring in, and the island grows or shrinks with a spring to fit.</summary>
    private void SetClockMode(ClockMode mode)
    {
        if (mode == _clockMode) return;
        var order = (int)mode - (int)_clockMode; // direction of travel decides which way things slide
        var old = RegionFor(_clockMode);
        _clockMode = mode;
        var incoming = RegionFor(mode);

        // outgoing: stays on screen for a moment without taking layout room (same trick as page changes)
        if (_state.IsExpanded && old.Visibility == Visibility.Visible && old.ActualHeight > 0)
        {
            _leavingClockRegion = old;
            old.IsHitTestVisible = false;
            old.Margin = new Thickness(old.Margin.Left, old.Margin.Top, old.Margin.Right, -old.ActualHeight);
            var oldShift = ShiftOf(old);
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) =>
            {
                if (_leavingClockRegion == old) _leavingClockRegion = null;
                old.BeginAnimation(OpacityProperty, null);
                oldShift.BeginAnimation(TranslateTransform.XProperty, null);
                old.Opacity = 1;
                old.IsHitTestVisible = true;
                old.Margin = new Thickness(old.Margin.Left, old.Margin.Top, old.Margin.Right, 0);
                if (old != RegionFor(_clockMode)) old.Visibility = Visibility.Collapsed;
            };
            old.BeginAnimation(OpacityProperty, fade);
            oldShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -18 * Math.Sign(order), TimeSpan.FromMilliseconds(150)));
        }

        ApplyClockMode();
        if (_state.IsExpanded)
        {
            var shift = ShiftOf(incoming);
            var scale = ScaleOf(incoming);
            var spring = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 };
            incoming.Opacity = 0;
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22 * Math.Sign(order), 0, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = spring });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = spring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = spring });
            Dispatcher.InvokeAsync(Render); // the island springs to the new height
        }
    }

    private static readonly string[] ModeNames = { "Alarm", "Timer", "Stopwatch" };

    /// <summary>Header row: the current mode on the left, the other two as small glass pills on the right.</summary>
    private ClockMode? _barMode;

    private void UpdateModeBar()
    {
        if (_barMode == _clockMode && ClockModeBar.Children.Count > 0) return; // keep the pills (and their animation state)
        _barMode = _clockMode;
        ClockModeBar.Children.Clear();
        ClockModeBar.ColumnDefinitions.Clear();
        ClockModeBar.ColumnDefinitions.Add(new ColumnDefinition());
        ClockModeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = _clockMode switch { ClockMode.Timer => "\uE916", ClockMode.Stopwatch => "\uE916", _ => "\uE121" };
        var tint = _clockMode == ClockMode.Stopwatch ? (Brush)FindResource("Cyan") : (Brush)FindResource("Orange");
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = glyph, FontSize = 14, Foreground = tint, Margin = new Thickness(0, 0, 8, 0) });
        title.Children.Add(new TextBlock { Text = ModeNames[(int)_clockMode], FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        ClockModeBar.Children.Add(title);

        var others = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (ClockMode m in Enum.GetValues<ClockMode>())
        {
            if (m == _clockMode) continue;
            var mode = m;
            var pill = Pill(ModeNames[(int)m], () => SetClockMode(mode));
            pill.RenderTransformOrigin = new Point(0.5, 0.5);
            pill.RenderTransform = new ScaleTransform(1, 1);
            // soft hover swell, like the other buttons
            pill.MouseEnter += (_, _) => PopPill(pill, 1.07);
            pill.MouseLeave += (_, _) => PopPill(pill, 1);
            others.Children.Add(pill);
        }
        Grid.SetColumn(others, 1);
        ClockModeBar.Children.Add(others);
    }

    private static void PopPill(Border pill, double to)
    {
        if (pill.RenderTransform is not ScaleTransform st) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(320)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>The idle stopwatch: one big Start button that counts 3-2-1 before it begins.</summary>
    private void BuildStopwatchIdle()
    {
        var hint = new TextBlock { Text = "Starts after a 3 second countdown", FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        _swStartText = new TextBlock { Text = "Start", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x16, 0x1B)), HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 54, TextAlignment = TextAlignment.Center };
        _swStartPill = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 8, 18, 8),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new LinearGradientBrush(Color.FromRgb(0xF7, 0xF8, 0xFC), Color.FromRgb(0xDD, 0xE2, 0xEE), 90),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Child = _swStartText,
        };
        _swStartPill.MouseEnter += (_, _) => PopPill(_swStartPill, 1.06);
        _swStartPill.MouseLeave += (_, _) => PopPill(_swStartPill, 1);
        _swStartPill.MouseLeftButtonUp += (_, e) => { e.Handled = true; StartStopwatchCountdown(); };
        StopwatchIdlePanel.Children.Add(hint);
        StopwatchIdlePanel.Children.Add(_swStartPill);
    }

    private void StartStopwatchCountdown()
    {
        if (_swCountdown is { IsEnabled: true } || _swStartText is null || _swStartPill is null) return;
        _swCount = 3;
        void Show()
        {
            _swStartText.Text = _swCount.ToString();
            // each number pops in and settles
            var st = (ScaleTransform)_swStartPill.RenderTransform;
            var pop = new DoubleAnimation(1.28, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        Show();
        _swCountdown = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _swCountdown.Tick += (_, _) =>
        {
            _swCount--;
            if (_swCount > 0) { Show(); return; }
            _swCountdown!.Stop();
            _swStartText.Text = "Start";
            _stopwatch.Start();
            if (_state.IsExpanded) Dispatcher.InvokeAsync(() => { ApplyClockMode(); Render(); });
        };
        _swCountdown.Start();
    }
}

