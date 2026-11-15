// D-Island (personal fork) - places the pet beneath the collapsed island.

using System.Windows;
using DIsland.Interop;

namespace DIsland;

public partial class MainWindow
{
    private readonly System.Windows.Threading.DispatcherTimer _petWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private void WirePet()
    {
        _petWatch.Tick += (_, _) => WatchCursor();
        _focusSession.Changed += () => _pet.Studying = _focusSession.Phase == FocusPhase.Focus && _focusSession.Running;
    }

    /// <summary>Tells the pet where the cursor is, so it can look at it, follow it and wake up for it.</summary>
    private void WatchCursor()
    {
        if (_pet.Visibility != Visibility.Visible || !NativeMethods.GetCursorPos(out var pt)) return;
        var p = _pet.PointFromScreen(new Point(pt.X, pt.Y));
        var dx = p.X - PetWidget.SpriteWidth / 2;
        var dy = p.Y - PetWidget.SpriteHeight / 2;
        _pet.Observe(dx, dy, Math.Sqrt(dx * dx + dy * dy) < 150);
    }

    private void ApplyPetSettings()
    {
        if (_pet is null) return;
        _pet.Kind = _settings.PetKind;
        _pet.Talks = _settings.PetTalks;
        _pet.Outfit = _settings.PetOutfit;
        _pet.Reminders = _settings.PetReminders;
        _pet.ReminderMinutes = _settings.PetReminderMinutes;
        if (!_settings.PetEnabled) { _pet.Visibility = Visibility.Collapsed; _pet.Running = false; }
    }

    /// <summary>Called with the pill's current width and height on every geometry update.</summary>
    private void PlacePet(double w, double h)
    {
        if (_pet is null) return;
        var show = _settings.PetEnabled && !_state.IsExpanded && !IslandHidden && !BarIsIdle && w > 40 && h < 80 && QuickTray.Visibility != Visibility.Visible;
        if (!show)
        {
            if (_pet.Visibility == Visibility.Visible) { _pet.Visibility = Visibility.Collapsed; _pet.Running = false; _petWatch.Stop(); }
            return;
        }
        var left = (BaseWidth - w) / 2 - 22;
        _pet.Margin = new Thickness(left, PillTop + h - 1, 0, 0);
        _pet.SetRange(0, w + 44);
        if (_pet.Visibility != Visibility.Visible) { _pet.Visibility = Visibility.Visible; _pet.Running = true; _petWatch.Start(); }
    }

    /// <summary>True when the cursor is over any visible piece of the island: bubbles, the pet, the quick tray.</summary>
    private bool OverIslandParts(int sx, int sy)
    {
        var p = new Point(sx, sy);
        foreach (FrameworkElement part in new FrameworkElement[] { MediaDiscBubble, MediaPulseBubble, ActivityBubble, ActivityBubble2, IdleWeatherBubble, CarryBubble, PinnedTimerBubble, QuickTray, _pet })
        {
            if (part.Visibility != Visibility.Visible || part.ActualWidth <= 0) continue;
            var local = part.PointFromScreen(p);
            if (local.X >= -2 && local.X <= part.ActualWidth + 2 && local.Y >= -2 && local.Y <= part.ActualHeight + 2) return true;
        }
        return false;
    }
}
