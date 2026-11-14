// D-Island (personal fork) - dismiss and snooze for notifications and alarms.

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private IslandNotification? _shownNotification;
    private readonly List<(DateTime Due, IslandNotification Note)> _snoozed = new();
    private readonly DispatcherTimer _snoozeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Alarm? _ringingAlarm;

    private void WireSnooze()
    {
        _snoozeTimer.Tick += (_, _) =>
        {
            var due = _snoozed.Where(s => s.Due <= DateTime.Now).ToList();
            foreach (var s in due) { _snoozed.Remove(s); OnNotificationArrived(s.Note); }
            if (_snoozed.Count == 0) _snoozeTimer.Stop();
        };
        NotificationView.MouseEnter += (_, _) => { if (_pendingCode is null) { NotifActions.Visibility = Visibility.Visible; NotifApp.Visibility = Visibility.Collapsed; } };
        NotificationView.MouseLeave += (_, _) => { NotifActions.Visibility = Visibility.Collapsed; if (_pendingCode is null) NotifApp.Visibility = Visibility.Visible; };
    }

    private void OnNotifDismissClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_shownNotification is { } n) _notifications.Dismiss(n.Id);
        _state.DismissTransient();
        HideCarry();
    }

    private void OnNotifSnoozeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_shownNotification is { } n)
        {
            _snoozed.Add((DateTime.Now.AddMinutes(Math.Max(1, _settings.SnoozeMinutes)), n));
            _snoozeTimer.Start();
        }
        _state.DismissTransient();
        HideCarry();
    }

    // ---------------------------------------------------------------- alarms

    /// <summary>Called when an alarm starts ringing: the pill gets Snooze and Stop.</summary>
    private void ShowAlarmActions(Alarm alarm)
    {
        _ringingAlarm = alarm;
        AlarmSnoozeText.Text = $"Snooze {Math.Max(1, _settings.SnoozeMinutes)}m";
        AlarmActions.Visibility = Visibility.Visible;
        TimerDoneView.Width = 420;
    }

    private void HideAlarmActions()
    {
        if (AlarmActions.Visibility != Visibility.Visible) return;
        AlarmActions.Visibility = Visibility.Collapsed;
        TimerDoneView.Width = 320;
        _ringingAlarm = null;
    }

    private void OnAlarmStopClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StopAlarmSound();
        _state.DismissTransient();
    }

    private void OnAlarmSnoozeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StopAlarmSound();
        var minutes = Math.Max(1, _settings.SnoozeMinutes);
        var at = DateTime.Now.AddMinutes(minutes);
        var label = (_ringingAlarm?.Label ?? "").Replace(" (snoozed)", "").Trim();
        _settings.Alarms.RemoveAll(a => a.Label.EndsWith("(snoozed)") && !a.Enabled);
        _settings.Alarms.Add(new Alarm { Time = at.ToString("HH:mm"), Enabled = true, Daily = false, Label = (label.Length > 0 ? label : "Alarm") + " (snoozed)" });
        _settings.Save();
        _alarms.Reschedule();
        _state.DismissTransient();
    }
}
