// Lumen (personal fork) - alarms on the Home page, right under the timer: pick a time, press Set, tap an alarm to remove it.

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
    private TextBlock _alarmTime = null!;
    private WrapPanel _alarmChips = null!;
    private Button _dailyChip = null!;

    private static string Clock(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("h:mm tt");

    private Button AlarmChip(object content, Action click, double minWidth = 0)
    {
        var button = new Button { Style = (Style)FindResource("ChipButton"), Content = content, Margin = new Thickness(0, 0, 4, 0), MinWidth = minWidth };
        button.Click += (_, _) => click();
        return button;
    }

    private void WireAlarmRow()
    {
        // start the picker at the next full half hour
        var now = DateTime.Now;
        _alarmMinutes = ((now.Hour * 60 + now.Minute) / 30 + 1) * 30 % 1440;

        _alarmTime = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 66, TextAlignment = TextAlignment.Center };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 14, Foreground = (Brush)FindResource("Orange"), Margin = new Thickness(0, 0, 8, 0) });
        var picker = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        picker.Children.Add(AlarmChip("−1h", () => Nudge(-60)));
        picker.Children.Add(AlarmChip("−5m", () => Nudge(-5)));
        picker.Children.Add(_alarmTime);
        picker.Children.Add(AlarmChip("+5m", () => Nudge(5)));
        picker.Children.Add(AlarmChip("+1h", () => Nudge(60)));
        _dailyChip = AlarmChip("Daily", () => { _alarmDaily = !_alarmDaily; UpdateAlarmRow(); });
        picker.Children.Add(_dailyChip);
        picker.Children.Add(AlarmChip("Set", AddAlarmFromRow));
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        _alarmChips = new WrapPanel { Margin = new Thickness(22, 6, 0, 0) };
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
        _alarmTime.Text = Clock(_alarmMinutes);
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

    private void BuildAlarmChips()
    {
        if (_alarmChips is null) return;
        _alarmChips.Children.Clear();
        foreach (var alarm in _settings.Alarms.OrderBy(a => a.TimeOfDay).ToList())
        {
            var text = Clock((int)alarm.TimeOfDay.TotalMinutes) + (alarm.Daily ? " · daily" : "") + (alarm.Enabled ? "" : " · off");
            var chip = AlarmChip(text + "  ✕", () =>
            {
                _settings.Alarms.Remove(alarm);
                _settings.Save();
                _alarms.Reschedule();
                BuildAlarmChips();
            });
            chip.ToolTip = "Click to remove";
            chip.Opacity = alarm.Enabled ? 1 : 0.5;
            chip.Margin = new Thickness(0, 0, 4, 4);
            _alarmChips.Children.Add(chip);
        }
        _alarmChips.Visibility = _alarmChips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Home) Dispatcher.InvokeAsync(Render);
    }
}
