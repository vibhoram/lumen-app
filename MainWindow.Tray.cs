// D-Island (personal fork) - the quick tray: hover the collapsed pill and a small glass tray appears under it with music
// controls and what you just copied or received. Tiles can be clicked (re-copies as the newest copy) or dragged out.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private readonly DispatcherTimer _trayWatch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private DateTime _trayAwayAt;
    private Point _trayDragStart;

    private void WireTray() => _trayWatch.Tick += (_, _) =>
    {
        if (QuickTray.Visibility != Visibility.Visible) { _trayWatch.Stop(); return; }
        if (IsCursorOver(QuickTray) || IsCursorOverPill()) { _trayAwayAt = DateTime.Now; return; }
        if ((DateTime.Now - _trayAwayAt).TotalMilliseconds >= Math.Clamp(_settings.TrayLingerMs, 300, 6000)) HideTray();
    };

    private static bool IsCursorOver(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || !NativeMethods.GetCursorPos(out var pt)) return false;
        var p = element.PointFromScreen(new Point(pt.X, pt.Y));
        return p.X >= -8 && p.X <= element.ActualWidth + 8 && p.Y >= -8 && p.Y <= element.ActualHeight + 8;
    }

    private IEnumerable<ClipItem> RecentClips() =>
        _clipHistory.Items.Where(i => (DateTime.Now - i.Time).TotalSeconds < 120).Take(3);

    private bool RecentNotification() => _shownNotification is not null && CarryVisible;

    /// <summary>True when there is something worth showing in the tray.</summary>
    private bool TrayHasContent() => _media.Current is not null || RecentClips().Any() || RecentNotification();

    private void ShowTray()
    {
        TrayRow.Children.Clear();
        var hasMedia = _media.Current is { } media;
        if (hasMedia)
        {
            TrayRow.Children.Add(TrayButton("\uE892", "Previous", () => _media.Previous()));
            var playing = _media.Current!.IsPlaying;
            var play = TrayButton(playing ? "\uE769" : "\uE768", playing ? "Pause" : "Play", () => { });
            play.MouseLeftButtonUp += (_, _) =>
            {
                _media.TogglePlayPause();
                // flip the glyph at once; the real state follows
                ((TextBlock)play.Child).Text = ((TextBlock)play.Child).Text == "\uE769" ? "\uE768" : "\uE769";
            };
            TrayRow.Children.Add(play);
            TrayRow.Children.Add(TrayButton("\uE893", "Next", () => _media.Next()));
        }

        var clips = RecentClips().ToList();
        if (clips.Count > 0 || RecentNotification())
        {
            if (hasMedia) TrayRow.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) });
            if (RecentNotification()) TrayRow.Children.Add(NotificationTile());
            foreach (var clip in clips) TrayRow.Children.Add(ClipTile(clip));
        }
        if (TrayRow.Children.Count == 0) return;

        QuickTray.Visibility = Visibility.Visible;
        QuickTray.UpdateLayout();
        _trayAwayAt = DateTime.Now;
        _trayWatch.Start();
        ApplyGeometry();

        var scale = (ScaleTransform)QuickTray.RenderTransform;
        var spring = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        QuickTray.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void HideTray()
    {
        _trayWatch.Stop();
        if (QuickTray.Visibility != Visibility.Visible) return;
        QuickTray.Visibility = Visibility.Collapsed;
    }

    private void PlaceTray(double w, double h)
    {
        if (QuickTray is null || QuickTray.Visibility != Visibility.Visible) return;
        QuickTray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = QuickTray.DesiredSize.Width;
        QuickTray.Margin = new Thickness((BaseWidth - width) / 2, PillTop + h + 7, 0, 0);
    }

    private static Border RoundBase(double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
        Margin = new Thickness(2, 0, 2, 0),
        Cursor = Cursors.Hand,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new ScaleTransform(1, 1),
    };

    private Border TrayButton(string glyph, string tip, Action click)
    {
        var button = RoundBase(36);
        button.ToolTip = tip;
        button.Child = new TextBlock { Text = glyph, FontFamily = AssistantPanel.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        button.MouseEnter += (_, _) => { button.Background = new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)); AssistantPanel.PopScale(button, 1.08); };
        button.MouseLeave += (_, _) => { button.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)); AssistantPanel.PopScale(button, 1); };
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    private Border NotificationTile()
    {
        var n = _shownNotification!;
        var tile = RoundBase(36);
        tile.ToolTip = $"{n.AppName}: {FirstLine(n.Title)}";
        tile.Child = n.AppIcon is { } icon
            ? new System.Windows.Shapes.Ellipse { Margin = new Thickness(5), Fill = new ImageBrush(icon) { Stretch = Stretch.UniformToFill } }
            : new TextBlock { Text = "\uEA8F", FontFamily = AssistantPanel.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            HideTray();
            SetExpanded(true);
            if (AvailablePages().Contains(ExpandedPage.Notifications)) ShowPage(ExpandedPage.Notifications, animate: false);
        };
        return tile;
    }

    /// <summary>A recent copy. Click it to make it the newest copy again; drag it into any app.</summary>
    private Border ClipTile(ClipItem item)
    {
        var tile = RoundBase(36);
        tile.CornerRadius = new CornerRadius(10);
        tile.ToolTip = item.Kind switch
        {
            ClipKind.Image => "Image: click to copy again, drag to drop",
            ClipKind.Link => item.Text.Trim(),
            _ => item.Text.Length > 80 ? item.Text[..80] + "…" : item.Text,
        };
        if (item.Kind == ClipKind.Image && _clipHistory.LoadImage(item, 80) is { } thumb)
            tile.Child = new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Margin = new Thickness(1), Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill } };
        else
            tile.Child = new TextBlock
            {
                Text = item.Kind switch { ClipKind.Link => "\uE71B", ClipKind.Files => "\uE8B7", _ => "\uE8C8" },
                FontFamily = AssistantPanel.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };

        var dragging = false;
        tile.PreviewMouseLeftButtonDown += (_, e) => { _trayDragStart = e.GetPosition(this); dragging = false; };
        tile.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragging) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _trayDragStart.X) < 6 && Math.Abs(p.Y - _trayDragStart.Y) < 6) return;
            dragging = true;
            var data = new DataObject();
            switch (item.Kind)
            {
                case ClipKind.Image:
                    if (_clipHistory.LoadImage(item) is { } full) data.SetImage(full);
                    var files = new System.Collections.Specialized.StringCollection { _clipHistory.ImagePath(item) };
                    data.SetFileDropList(files); // chat boxes and folders take the picture as a file
                    break;
                case ClipKind.Files:
                    var list = new System.Collections.Specialized.StringCollection();
                    list.AddRange(item.Text.Split('\n'));
                    data.SetFileDropList(list);
                    break;
                default:
                    data.SetText(item.Text);
                    break;
            }
            try { DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy); } catch (Exception) { }
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (dragging) { dragging = false; return; }
            _clipHistory.Restore(item); // the watcher sees an identical item and just moves it to the top
            _pet.React(PetEvent.Copy);
            var tick = new TextBlock { Text = "\uE73E", FontFamily = AssistantPanel.IconFont, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { CornerRadius = tile.CornerRadius, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x30, 0xD1, 0x58)), Child = tick };
            var old = tile.Child;
            tile.Child = back;
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            restore.Tick += (_, _) => { restore.Stop(); tile.Child = old; };
            restore.Start();
        };
        return tile;
    }
}
