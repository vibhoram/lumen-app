// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows.Threading;

namespace DIsland;

public enum IslandView
{
    Clock,
    Media,
    Timer,
    Stopwatch,
    Volume,
    Brightness,
    Battery,
    TimerDone,
    /// <summary>Short info alert (device, lock key, Focus, clipboard); all share one view.</summary>
    Info,
    /// <summary>Another app's notification.</summary>
    Notification,
    /// <summary>An alarm is set for the next few hours: only ever shown as a bubble beside the pill, never as the pill itself.</summary>
    AlarmSet,
}

public static class TransientPriority
{
    public const int Osd = 1;
    public const int Clipboard = 1;
    public const int LockKey = 1;
    public const int Device = 2;
    public const int Focus = 2;
    public const int Calendar = 3;
    public const int Notification = 3;
    public const int Battery = 2;
    public const int LowBattery = 3;
    public const int TimerDone = 4;
    public const int Alarm = 5;
}

/// <summary>
/// Decides what the island shows. Transient states (OSD, alerts) win for a few seconds, then
/// the island falls back to the highest persistent state: timer > stopwatch > media playing > clock
/// (the next running activity shows as a bubble beside it, see <see cref="SecondaryView"/>).
/// A user expansion overrides both.
/// </summary>
public sealed class StateController
{
    private readonly DispatcherTimer _transientTimer = new();
    private IslandView? _transient;
    private int _transientPriority;
    private bool _timerActive;
    private bool _stopwatchActive;
    private bool _mediaPlaying;
    private bool _alarmArmed;
    private bool _expanded;

    public StateController()
    {
        _transientTimer.Tick += (_, _) => DismissTransient();
    }

    public event Action? Changed;

    public bool TimerActive { get => _timerActive; set => Set(ref _timerActive, value); }
    public bool StopwatchActive { get => _stopwatchActive; set => Set(ref _stopwatchActive, value); }
    public bool MediaPlaying { get => _mediaPlaying; set => Set(ref _mediaPlaying, value); }
    public bool AlarmArmed { get => _alarmArmed; set => Set(ref _alarmArmed, value); }
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public IslandView? Transient => _transient;

    /// <summary>Running activities, most important first: timer > stopwatch > media.</summary>
    private IEnumerable<IslandView> Activities()
    {
        // Music keeps the pill; a timer or stopwatch rides along as a round bubble with its time inside.
        if (_mediaPlaying) yield return IslandView.Media;
        if (_timerActive) yield return IslandView.Timer;
        if (_stopwatchActive) yield return IslandView.Stopwatch;
        if (_alarmArmed) yield return IslandView.AlarmSet;
    }

    /// <summary>The pill itself: music while it plays, otherwise the clock. A timer, stopwatch or alarm never takes the pill over; each rides beside it as a bubble.</summary>
    public IslandView PersistentView => _mediaPlaying ? IslandView.Media : IslandView.Clock;

    /// <summary>Everything running except the activity that owns the pill: these become bubbles.</summary>
    private IEnumerable<IslandView> Bubbles()
    {
        var main = PersistentView;
        return Activities().Where(v => v != main);
    }

    /// <summary>The next activity after the one in the pill, shown as a bubble beside it (like the iPhone's split island).</summary>
    public IslandView? SecondaryView => Bubbles().Select(v => (IslandView?)v).FirstOrDefault();

    /// <summary>The third running activity, shown as a second bubble.</summary>
    public IslandView? TertiaryView => Bubbles().Skip(1).Select(v => (IslandView?)v).FirstOrDefault();

    public IslandView CurrentView => _transient ?? PersistentView;

    /// <summary>
    /// Shows a transient view. A lower-priority transient cannot interrupt a higher one that is
    /// still on screen; equal or higher priority replaces it and restarts the countdown.
    /// </summary>
    public bool ShowTransient(IslandView view, int priority, TimeSpan duration)
    {
        if (!CanShowTransient(priority)) return false;

        _transient = view;
        _transientPriority = priority;
        _transientTimer.Stop();
        _transientTimer.Interval = duration;
        _transientTimer.Start();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Would <see cref="ShowTransient"/> at this priority be shown (so a view can be filled in first)?</summary>
    public bool CanShowTransient(int priority) => _transient is null || priority >= _transientPriority;

    public void DismissTransient()
    {
        _transientTimer.Stop();
        if (_transient is null) return;
        _transient = null;
        _transientPriority = 0;
        Changed?.Invoke();
    }

    private void Set(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Changed?.Invoke();
    }
}
