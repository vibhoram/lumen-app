// Lumen (personal fork) - alarms on the Home page, right under the timer. One compact row:
//   bell  [ 7 : 30  AM ]  Daily  Set      (scroll the wheel over the hour or the minutes; click AM/PM to flip)
// Your alarms sit on a single quiet line below; tap one to remove it.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var now = DateTime.Now;
        _alarmMinutes = ((now.Hour * 60 + now.Minute) / 30 + 1) * 30 % 1440; // the next half hour

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
        row.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 14, Foreground = (Brush)FindResource("Orange"), Margin = new Thickness(0, 0, 10, 0) });
        Grid.SetColumn(time, 1);
        row.Children.Add(time);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _dailyChip = Pill("Daily", () => { _alarmDaily = !_alarmDaily; UpdateAlarmRow(); });
        actions.Children.Add(_dailyChip);
        actions.Children.Add(Pill("Set alarm", AddAlarmFromRow));
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        _alarmChips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 5, 0, 0) };
        AlarmRow.Children.Add(row);
        AlarmRow.Children.Add(_alarmChips);
        _settings.Changed += BuildAlarmChips;
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
            ? new LinearGradientBrush(Color.FromRgb(0x8B, 0x6C, 0xFF), Color.FromRgb(0x2D, 0xD4, 0xFF), 35)
            : (Brush)FindResource("GlassFill");
    }

    private void AddAlarmFromRow()
    {
        _settings.Alarms.Add(new Alarm { Time = TimeSpan.FromMinutes(_alarmMinutes).ToString(@"hh\:mm"), Enabled = true, Daily = _alarmDaily, Label = "Alarm" });
        _settings.Save();
        _alarms.Reschedule();
        BuildAlarmChips();
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
}
