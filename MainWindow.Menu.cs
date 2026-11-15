// D-Island (personal fork) - right-click menu, quiet mode, and the pinned timer.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private DateTime? _quietUntil;
    private bool _quietForever;
    private bool _timerPinned;
    private readonly DispatcherTimer _quietTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _pressTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    /// <summary>Quiet mode: notifications and small alerts stay out of the way (timers and alarms still ring).</summary>
    private bool Quiet => _quietForever || _quietUntil is { } until && DateTime.Now < until;

    private void WireMenu()
    {
        Root.MouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInside(source, _pet)) return; // the pet has its own menu
            e.Handled = true;
            ShowMenu();
        };
        _quietTimer.Tick += (_, _) => { if (!Quiet) EndQuiet(); };

        // long-press a timer (the pill or a bubble) to pin it beside the island
        _pressTimer.Tick += (_, _) =>
        {
            _pressTimer.Stop();
            if (_timer.IsActive) SetTimerPinned(!_timerPinned);
        };
        foreach (var element in new FrameworkElement[] { TimerView, ActivityBubble, ActivityBubble2, PinnedTimerBubble })
        {
            element.PreviewMouseLeftButtonDown += (_, _) => { if (_timer.IsActive) _pressTimer.Start(); };
            element.PreviewMouseLeftButtonUp += (_, _) => _pressTimer.Stop();
            element.MouseLeave += (_, _) => _pressTimer.Stop();
        }
    }

    private ContextMenu BuildMenu()
    {
        var dark = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2A));
        var menu = new ContextMenu { Background = dark, Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)), Padding = new Thickness(4) };
        MenuItem Item(string header, Action act, bool check = false)
        {
            var item = new MenuItem { Header = header, Background = dark, Foreground = Brushes.White, IsChecked = check };
            item.Click += (_, _) => act();
            return item;
        }
        if (Quiet) menu.Items.Add(Item("Turn quiet mode off", EndQuiet));
        else
        {
            menu.Items.Add(Item("Quiet for 30 minutes", () => StartQuiet(TimeSpan.FromMinutes(30))));
            menu.Items.Add(Item("Quiet for 1 hour", () => StartQuiet(TimeSpan.FromHours(1))));
            menu.Items.Add(Item("Quiet until I turn it off", () => StartQuiet(null)));
        }
        if (_timer.IsActive) menu.Items.Add(Item(_timerPinned ? "Unpin the timer" : "Pin the timer beside the island", () => SetTimerPinned(!_timerPinned)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hide the island", () => SetIslandHidden(true)));
        menu.Items.Add(Item("Settings", () => OpenSettings()));
        return menu;
    }

    private void ShowMenu()
    {
        var menu = BuildMenu();
        menu.PlacementTarget = Root;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ quiet mode

    private void StartQuiet(TimeSpan? length)
    {
        _quietForever = length is null;
        _quietUntil = length is { } l ? DateTime.Now + l : null;
        _quietTimer.Start();
        _state.DismissTransient();
        HideCarry();
        HideTray();
        _pet.Quiet = true;
        RefreshClockNow();
    }

    private void EndQuiet()
    {
        _quietForever = false;
        _quietUntil = null;
        _quietTimer.Stop();
        _pet.Quiet = false;
        RefreshClockNow();
    }

    private void RefreshClockNow()
    {
        _clockStyleKey = null; // forces UpdateClock to redraw (the moon shows while quiet)
        UpdateClock();
    }

    // ------------------------------------------------------------------ pinned timer

    private void SetTimerPinned(bool pin)
    {
        _timerPinned = pin && _timer.IsActive;
        UpdatePinnedTimer();
    }

    /// <summary>The pinned timer bubble: time left in the middle, a ring draining around it. Visible even while the island is open.</summary>
    private void UpdatePinnedTimer()
    {
        var show = _timerPinned && _timer.IsActive;
        if (!show && _timerPinned) _timerPinned = false;
        if (show)
        {
            PinnedTimerText.Text = TimerService.Format(_timer.Remaining);
            PinnedTimerRing.Stroke = _focusSession.Phase != FocusPhase.Idle ? RingFocus : RingTimer;
            var fraction = _timer.Total.TotalSeconds > 0 ? _timer.Remaining.TotalSeconds / _timer.Total.TotalSeconds : 0;
            PinnedTimerRing.Data = RingGeometry(Math.Max(30, PinnedTimerBubble.Width), Math.Clamp(fraction, 0, 1));
            PinnedTimerBubble.ToolTip = "Pinned timer. Long-press to unpin.";
        }
        if (ShowBubble(PinnedTimerBubble, show)) ApplyGeometry();
    }

    // ------------------------------------------------------------------ shortcuts

    /// <summary>Re-registers Clara, quick-paste, and per-page shortcuts when they change in Settings.</summary>
    private void UpdateShortcuts()
    {
        if (new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        PageHotkeyMap.EnsureDefaults(_settings.PageHotkeys, _settings.AssistantHotkey, _settings.ToggleHotkey, _settings.QuickPasteHotkey);

        // Always re-apply so TakenByAnotherApp + raw-input fallback stay correct after Attach.
        _assistHotkeyText = _settings.AssistantHotkey;
        var assistProblem = _assistHotkey.Set(_settings.AssistantHotkey);
        var paste = _settings.QuickPasteEnabled ? _settings.QuickPasteHotkey : "";
        _pasteHotkeyText = paste;
        var pasteProblem = _pasteHotkey.Set(paste);

        // Same raw-input fallback as hide/show: if another app already owns the combo,
        // RegisterHotKey fails but we still catch it from the keyboard stream.
        _keyboard.AssistShortcut = _assistHotkey.TakenByAnotherApp
            && Hotkey.TryParse(_settings.AssistantHotkey, out var am, out var ak)
            ? (am, KeyInterop.VirtualKeyFromKey(ak))
            : null;
        _keyboard.QuickPasteShortcut = _pasteHotkey.TakenByAnotherApp && paste.Length > 0
            && Hotkey.TryParse(paste, out var pm, out var pk)
            ? (pm, KeyInterop.VirtualKeyFromKey(pk))
            : null;

        RegisterPageHotkeys();
        _keyboard.Update();
        RefreshHotkeyProblem(assistProblem, pasteProblem);
    }

    private void RegisterPageHotkeys()
    {
        foreach (var (hook, _) in _pageHotkeys) hook.Dispose();
        _pageHotkeys.Clear();
        var extras = new List<(ModifierKeys Modifiers, int VirtualKey, Action Handler)>();
        var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (source is null) return;

        foreach (var name in PageHotkeyMap.PageNames)
        {
            if (!_settings.PageHotkeys.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            if (!Enum.TryParse<ExpandedPage>(name, out var page)) continue;
            var hook = new Hotkey(_nextPageHotkeyId++);
            hook.Attach(source);
            var problem = hook.Set(text);
            var captured = page;
            hook.Pressed += () => OnPageHotkey(captured);
            _pageHotkeys.Add((hook, name));
            if (hook.TakenByAnotherApp && Hotkey.TryParse(text, out var mod, out var key))
                extras.Add((mod, KeyInterop.VirtualKeyFromKey(key), () => OnPageHotkey(captured)));
            else if (problem is not null && _settings.HotkeyProblem is null)
                _settings.HotkeyProblem = problem;
        }
        _keyboard.SetExtraFallbacks(extras);
    }

    /// <summary>One Settings banner for every shortcut another app already owns.</summary>
    private void RefreshHotkeyProblem(string? assistProblem = null, string? pasteProblem = null)
    {
        var stolen = new List<string>();
        if (_keyboard.Shortcut is not null) stolen.Add(_settings.ToggleHotkey);
        if (_keyboard.AssistShortcut is not null) stolen.Add(_settings.AssistantHotkey);
        if (_keyboard.QuickPasteShortcut is not null) stolen.Add(_settings.QuickPasteHotkey);
        foreach (var (hook, page) in _pageHotkeys)
        {
            if (hook.TakenByAnotherApp && _settings.PageHotkeys.TryGetValue(page, out var text))
                stolen.Add($"{PageHotkeyMap.TitleOf(page)} ({text})");
        }
        if (stolen.Count > 0)
        {
            _settings.HotkeyProblem =
                $"Another app also uses {string.Join(", ", stolen)}. Lumen still responds, but that app may react too. Pick another shortcut to avoid that.";
            return;
        }
        _settings.HotkeyProblem = assistProblem ?? pasteProblem;
    }

    private void OnPasteHotkey()
    {
        if (!_settings.QuickPasteEnabled) return;
        HoldOpenFromHotkey(8); // window is separate, but keep island from collapsing under a leave-poll race
        new QuickPasteWindow(_clipHistory).Show();
    }

    /// <summary>Commands from the tray menu.</summary>
    public void TrayCommand(string id)
    {
        switch (id)
        {
            case "clara": OnAssistHotkey(); break;
            case "paste": OnPasteHotkey(); break;
            case "focus": _focusSession.StartFocusWithMinutes(25); break;
            case "quiet30": StartQuiet(TimeSpan.FromMinutes(30)); break;
            case "quiet60": StartQuiet(TimeSpan.FromHours(1)); break;
            case "quietforever": StartQuiet(null); break;
            case "quietoff": EndQuiet(); break;
            case "clippause":
                _clipHistory.Paused = !_clipHistory.Paused;
                ShowInfo(TransientPriority.Device, 300, "", _clipHistory.Paused ? "Clipboard on hold" : "Clipboard saving again", _clipHistory.Paused ? "nothing is saved" : "", seconds: 2);
                break;
            case "ram0": case "ram1": case "ram2":
            {
                var mode = id == "ram0" ? RamCleanMode.Normal : id == "ram1" ? RamCleanMode.More : RamCleanMode.Maximum;
                _ = VitalsService.CleanAsync(mode).ContinueWith(t => Dispatcher.InvokeAsync(() =>
                {
                    ShowInfo(TransientPriority.Device, 300, "\uE9D9", $"Freed {t.Result.FreedGb:0.0} GB", t.Result.Note, seconds: 3);
                    if (t.Result.FreedGb >= 0.3) _pet.React(PetEvent.RamFreed, $"{t.Result.FreedGb:0.0} GB");
                }));
                break;
            }
            case "timercancel": _timer.Cancel(); break;
            case "media:playpause": _media.TogglePlayPause(); break;
            case "media:next": _media.Next(); break;
            case "media:previous": _media.Previous(); break;
            default:
                if (id.StartsWith("timer") && int.TryParse(id[5..], out var minutes)) _timer.Start(TimeSpan.FromMinutes(minutes));
                else if (id.StartsWith("page:") && Enum.TryParse<ExpandedPage>(id[5..], out var page) && AvailablePages().Contains(page))
                {
                    HoldOpenFromHotkey();
                    if (IslandHidden) SetIslandHidden(false);
                    SetExpanded(true);
                    ShowPage(page, animate: true);
                }
                break;
        }
    }
}
