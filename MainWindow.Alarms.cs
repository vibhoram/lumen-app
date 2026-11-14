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
    private DrumPicker _aHour = null!, _aMinute = null!, _aAmPm = null!;
    private bool _drumSync;

    private StackPanel _alarmChips = null!;
    private Border _dailyChip = null!;

    private static string Clock(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("h:mm tt");

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

        // iOS-style dials: hour, minute, AM/PM (wheel, drag, or click above/below the middle)
        _aHour = new DrumPicker(44) { Items = Enumerable.Range(1, 12).Select(h => h.ToString()).ToList() };
        _aMinute = new DrumPicker(44) { Items = Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList() };
        _aAmPm = new DrumPicker(44) { Items = new[] { "AM", "PM" }, Loop = false };
        void FromDrums()
        {
            if (_drumSync) return;
            var h12 = _aHour.Index + 1;                       // 1..12
            var hour = h12 % 12 + (_aAmPm.Index == 1 ? 12 : 0);
            _alarmMinutes = hour * 60 + _aMinute.Index;
        }
        _aHour.ValueChanged += _ => FromDrums();
        _aMinute.ValueChanged += _ => FromDrums();
        _aAmPm.ValueChanged += _ => FromDrums();

        var time = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        time.Children.Add(_aHour);
        time.Children.Add(new TextBlock { Text = ":", FontSize = 20, FontWeight = FontWeights.SemiBold, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 3) });
        time.Children.Add(_aMinute);
        time.Children.Add(new Border { Width = 6 });
        time.Children.Add(_aAmPm);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(time, 1);
        row.Children.Add(time);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
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
        _settings.Changed += RefreshAlarmArmed;
        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => { UpdateModeStatus(); if (++_armedTicks % 20 == 0) RefreshAlarmArmed(); };
        tick.Start();
        RefreshAlarmArmed();
        BuildStopwatchIdle();
        UpdateAlarmRow();
        BuildAlarmChips();
    }

    private void UpdateAlarmRow()
    {
        var t = DateTime.Today.AddMinutes(_alarmMinutes);
        _drumSync = true;
        _aHour.Index = (t.Hour % 12 == 0 ? 12 : t.Hour % 12) - 1;
        _aMinute.Index = t.Minute;
        _aAmPm.Index = t.Hour >= 12 ? 1 : 0;
        _drumSync = false;
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

    private enum ClockMode { Alarm, Timer, Stopwatch, None }

    private ClockMode _clockMode = ClockMode.None; // collapsed until you open one
    private System.Windows.Threading.DispatcherTimer? _swCountdown;
    private int _swCount;
    private Border? _swStartPill;
    private TextBlock? _swStartText;

    private FrameworkElement? RegionFor(ClockMode mode) => mode switch
    {
        ClockMode.Alarm => AlarmRow,
        ClockMode.Timer => _timer.IsActive ? TimerActivePanel : TimerPresets,
        ClockMode.Stopwatch => _stopwatch.IsActive ? StopwatchPanel : StopwatchIdlePanel,
        _ => null,
    };

    /// <summary>Shows only the options of the current mode (no animation: used on every refresh).</summary>
    private void ApplyClockMode()
    {
        if (_clockMode == ClockMode.Timer) { BuildTimerPresets(); BuildTimerDial(); }
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
        if (mode == _clockMode) mode = ClockMode.None; // tapping the open one closes it
        var order = mode == ClockMode.None ? 1 : (int)mode - (int)(_clockMode == ClockMode.None ? ClockMode.Alarm : _clockMode); // direction of travel decides which way things slide
        var old = RegionFor(_clockMode);
        _clockMode = mode;
        var incoming = RegionFor(mode);

        // outgoing: stays on screen for a moment without taking layout room (same trick as page changes)
        if (old is not null && _state.IsExpanded && old.Visibility == Visibility.Visible && old.ActualHeight > 0)
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
        if (incoming is null) { if (_state.IsExpanded) Dispatcher.InvokeAsync(Render); }
        else if (_state.IsExpanded)
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
    private static readonly string[] ModeGlyphs = { "\uE121", "\uE916", "\uE916" };

    private ClockMode? _barMode;

    /// <summary>One row, three pills. The open one is lit; tap it again to fold everything away.</summary>
    private void UpdateModeBar()
    {
        if (_barMode == _clockMode && ClockModeBar.Children.Count > 0) return; // keep the pills (and their animation state)
        _barMode = _clockMode;
        ClockModeBar.Children.Clear();
        ClockModeBar.ColumnDefinitions.Clear();
        for (var i = 0; i < 3; i++) ClockModeBar.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 3; i++)
        {
            var mode = (ClockMode)i;
            var open = _clockMode == mode;
            var tint = open ? Brushes.White : (Brush)FindResource("Secondary");
            var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = ModeGlyphs[i], FontSize = 13, Foreground = open ? Brushes.White : (i == 2 ? (Brush)FindResource("Cyan") : (Brush)FindResource("Orange")), Margin = new Thickness(0, 0, 7, 0) });
            label.Children.Add(new TextBlock { Text = ModeNames[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = tint, VerticalAlignment = VerticalAlignment.Center });
            var status = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = i == 2 ? (Brush)FindResource("Cyan") : (Brush)FindResource("Orange") };
            _modeStatus[i] = status;
            label.Children.Add(status);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("GlassRim"),
                Background = open ? AssistantPanel.AccentGradient() : (Brush)FindResource("GlassFill"),
                Child = label,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            pill.MouseEnter += (_, _) => PopPill(pill, 1.05);
            pill.MouseLeave += (_, _) => PopPill(pill, 1);
            pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetClockMode(mode); };
            Grid.SetColumn(pill, i);
            ClockModeBar.Children.Add(pill);
        }
        UpdateModeStatus();
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

    // ------------------------------------------------------------------ custom timer dial

    private DrumPicker? _tH, _tM, _tS;
    private StackPanel? _timerDial;
    private TimeSpan _timerDraft = TimeSpan.FromMinutes(10);

    /// <summary>Hours : minutes : seconds dials and a Start button; the one-tap lengths sit under them.</summary>
    private void BuildTimerDial()
    {
        if (_timerDial is not null) return;
        _tH = new DrumPicker(40) { Items = Enumerable.Range(0, 24).Select(h => h.ToString()).ToList() };
        _tM = new DrumPicker(40) { Items = Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList() };
        _tS = new DrumPicker(40) { Items = Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList() };
        _tH.Index = _timerDraft.Hours; _tM.Index = _timerDraft.Minutes; _tS.Index = _timerDraft.Seconds;
        void Colon(StackPanel p) => p.Children.Add(new TextBlock { Text = ":", FontSize = 20, FontWeight = FontWeights.SemiBold, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 3) });
        var dials = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dials.Children.Add(_tH); Colon(dials); dials.Children.Add(_tM); Colon(dials); dials.Children.Add(_tS);
        dials.Children.Add(new TextBlock { Text = "h  m  s", FontSize = 10.5, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });

        var start = Pill("Start", () =>
        {
            var span = new TimeSpan(_tH!.Index, _tM!.Index, _tS!.Index);
            if (span.TotalSeconds < 1) return;
            _timerDraft = span;
            _timer.Start(span);
            if (span.TotalMinutes >= 1) SmartTime.RecordTimer(_settings, (int)Math.Round(span.TotalMinutes));
            _settings.Save();
        });
        start.HorizontalAlignment = HorizontalAlignment.Right;
        start.VerticalAlignment = VerticalAlignment.Center;
        start.Background = AssistantPanel.AccentGradient();
        ((TextBlock)start.Child).FontSize = 13;
        start.Padding = new Thickness(18, 7, 18, 7);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(dials);
        Grid.SetColumn(start, 1);
        row.Children.Add(start);
        _timerDial = new StackPanel();
        _timerDial.Children.Add(row);

        // TimerPresets is a one-cell grid holding the chip row: stack the dial above it
        TimerPresets.RowDefinitions.Clear();
        TimerPresets.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TimerPresets.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_timerDial, 0);
        Grid.SetRow(TimerPresetRow, 1);
        TimerPresets.Children.Add(_timerDial);
    }

    // ------------------------------------------------------------------ what is running, shown outside the open panel

    private DateTime? _nextAlarmAt;
    private int _armedTicks;
    private readonly TextBlock?[] _modeStatus = new TextBlock?[3];

    /// <summary>The next enabled alarm in the coming 12 hours: it becomes a small bubble beside the collapsed pill.</summary>
    private void RefreshAlarmArmed()
    {
        var now = DateTime.Now;
        DateTime? next = null;
        foreach (var alarm in _settings.Alarms.Where(a => a.Enabled))
        {
            var at = now.Date + alarm.TimeOfDay;
            if (at <= now) at = at.AddDays(1);
            if (next is null || at < next) next = at;
        }
        _nextAlarmAt = next;
        var armed = next is { } due && (due - now).TotalHours <= 12;
        if (_state is not null && _state.AlarmArmed != armed) _state.AlarmArmed = armed;
        else if (armed && IsLoaded && !_state!.IsExpanded) UpdateSecondActivity(); // refresh the time on the bubble
        UpdateModeStatus();
    }

    /// <summary>Live status inside each pill of the Alarm / Timer / Stopwatch row, so a running timer is never hidden when its panel is closed.</summary>
    private void UpdateModeStatus()
    {
        if (_modeStatus[0] is null) return;
        _modeStatus[0]!.Text = _nextAlarmAt is { } at && (at - DateTime.Now).TotalHours <= 24 ? at.ToString("h:mm tt") : "";
        _modeStatus[1]!.Text = _timer.IsActive ? TimerService.Format(_timer.Remaining) : "";
        _modeStatus[2]!.Text = _stopwatch.IsActive ? StopwatchService.Format(_stopwatch.Elapsed, tenths: false) : "";
    }
}
