// D-Island (personal fork) - places the pet beneath the collapsed island.

using System.Windows;

namespace DIsland;

public partial class MainWindow
{
    private void ApplyPetSettings()
    {
        if (_pet is null) return;
        _pet.Kind = _settings.PetKind;
        if (!_settings.PetEnabled) { _pet.Visibility = Visibility.Collapsed; _pet.Running = false; }
    }

    /// <summary>Called with the pill's current width and height on every geometry update.</summary>
    private void PlacePet(double w, double h)
    {
        if (_pet is null) return;
        var show = _settings.PetEnabled && !_state.IsExpanded && !IslandHidden && w > 40 && h < 80;
        if (!show)
        {
            if (_pet.Visibility == Visibility.Visible) { _pet.Visibility = Visibility.Collapsed; _pet.Running = false; }
            return;
        }
        var left = (BaseWidth - w) / 2 - 22;
        _pet.Margin = new Thickness(left, PillTop + h - 1, 0, 0);
        _pet.SetRange(0, w + 44);
        if (_pet.Visibility != Visibility.Visible) { _pet.Visibility = Visibility.Visible; _pet.Running = true; }
    }
}
