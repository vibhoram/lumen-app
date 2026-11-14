// D-Island (personal fork) - the collapsed music pill, split into pieces: a spinning album disc with a progress ring,
// the title pill, and a pulse bubble. Also the "carry" bubble that holds a notification or a copy for a few seconds.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private bool _discSpinning;

    /// <summary>Layout of the collapsed music view: split into disc + pill + pulse, or art and bars inside one pill.</summary>
    private void ApplyMusicLayout()
    {
        var split = _settings.MusicSplit;
        MediaView.Width = split ? 238 : 340;
        MediaArtSmall.Visibility = split ? Visibility.Collapsed : Visibility.Visible;
        MediaTitleClip.Margin = split ? new Thickness(16, 0, 6, 0) : new Thickness(11, 0, 6, 0);
        // The equalizer lives in the pulse bubble when split, otherwise back in the pill (right side).
        var target = split ? (Panel)MediaPulseBubble : MediaView;
        if (Equalizer.Parent is Panel parent && !ReferenceEquals(parent, target))
        {
            parent.Children.Remove(Equalizer);
            target.Children.Add(Equalizer);
            if (split) { Grid.SetColumn(Equalizer, 0); Equalizer.Margin = new Thickness(0); Equalizer.HorizontalAlignment = HorizontalAlignment.Center; }
            else { Grid.SetColumn(Equalizer, 3); Equalizer.Margin = new Thickness(0, 0, 14, 0); Equalizer.HorizontalAlignment = HorizontalAlignment.Right; }
        }
        UpdateMediaBubbles();
    }

    /// <summary>Shows the disc and pulse bubbles while the collapsed music pill is on screen.</summary>
    private void UpdateMediaBubbles()
    {
        if (_state is null || MediaDiscBubble is null) return;
        var show = _settings.MusicSplit && _currentView == MediaView && !_state.IsExpanded && !IslandHidden && _state.Transient is null
                   && _media.Current is not null;
        var changed = ShowBubble(MediaDiscBubble, show);
        changed |= ShowBubble(MediaPulseBubble, show);

        // the disc stays still (it used to spin, which was distracting)
        MediaDiscSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        if (show && _media.Current is { Duration.Ticks: > 0 } media)
        {
            MediaDiscRing.Stroke = RingMedia;
            MediaDiscRing.Data = RingGeometry(Math.Max(26, MediaDiscBubble.Width), Math.Clamp(media.EstimatePosition(DateTimeOffset.Now) / media.Duration, 0, 1));
        }
        else MediaDiscRing.Data = Geometry.Empty;
        if (changed) ApplyGeometry();
    }

    // ------------------------------------------------------------------ carry bubble

    private readonly System.Windows.Threading.DispatcherTimer _carryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _carryUntil;
    private int _carryCount;
    private ExpandedPage _carryPage = ExpandedPage.Notifications;
    private string? _carryApp;

    private bool CarryVisible => CarryBubble.Visibility == Visibility.Visible;

    private void WireCarry() => _carryTimer.Tick += (_, _) =>
    {
        if (DateTime.Now >= _carryUntil) HideCarry();
    };

    private void CarryNotification(IslandNotification n)
    {
        if (_settings.CarrySeconds <= 0 || IslandHidden) return;
        _carryCount = _carryApp == n.AppName && CarryVisible && _carryPage == ExpandedPage.Notifications ? _carryCount + 1 : 1;
        _carryApp = n.AppName;
        _carryPage = ExpandedPage.Notifications;
        if (n.AppIcon is { } icon)
        {
            CarryIcon.Fill = new ImageBrush(icon) { Stretch = Stretch.UniformToFill };
            CarryIcon.Visibility = Visibility.Visible;
            CarryGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            CarryIcon.Visibility = Visibility.Collapsed;
            CarryGlyph.Text = "\uEA8F"; // bell
            CarryGlyph.Visibility = Visibility.Visible;
        }
        CarryBubble.ToolTip = $"{n.AppName}: {FirstLine(n.Title)}";
        Carry();
    }

    private void CarryCopy(ClipboardEvent copy)
    {
        if (_settings.CarrySeconds <= 0 || IslandHidden) return;
        _carryCount = 0;
        _carryApp = null;
        _carryPage = ExpandedPage.Clipboard;
        CarryIcon.Visibility = Visibility.Collapsed;
        CarryGlyph.Visibility = Visibility.Visible;
        CarryGlyph.Text = copy.Kind switch { ClipboardKind.Image => "\uEB9F", ClipboardKind.Files => "\uE8B7", _ => "\uE77F" };
        CarryBubble.ToolTip = copy.Kind == ClipboardKind.Image ? "Copied an image" : "Copied: " + (copy.Preview ?? "text");
        Carry();
    }

    private void Carry()
    {
        CarryBadge.Visibility = _carryCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        CarryBadgeText.Text = _carryCount.ToString();
        _carryUntil = DateTime.Now.AddSeconds(Math.Clamp(_settings.CarrySeconds, 1, 20));
        _carryTimer.Start();
        if (ShowBubble(CarryBubble, !_state.IsExpanded)) ApplyGeometry();
    }

    private void HideCarry()
    {
        _carryTimer.Stop();
        if (CarryBubble.Visibility != Visibility.Visible) return;
        CarryBubble.Visibility = Visibility.Collapsed;
        ApplyGeometry();
    }

    private void OnCarryClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var page = _carryPage;
        HideCarry();
        SetExpanded(true);
        if (AvailablePages().Contains(page)) ShowPage(page, animate: false);
    }
}
