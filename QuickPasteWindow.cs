// D-Island (personal fork) - the quick paste picker: a small glass list of your latest copies at the cursor. Pick one (click,
// number key, or arrows + Enter) and it goes back on the clipboard as the newest copy and is pasted where you were typing.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public sealed class QuickPasteWindow : Window
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private readonly ClipboardHistory _history;
    private readonly IntPtr _previous;
    private readonly List<ClipItem> _items;
    private readonly List<Border> _rows = new();
    private int _selected;
    private bool _done;

    public QuickPasteWindow(ClipboardHistory history)
    {
        _history = history;
        _previous = GetForegroundWindow(); // where the paste should land
        _items = history.Items.Take(9).ToList();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Brushes.White;

        var list = new StackPanel { Margin = new Thickness(6) };
        if (_items.Count == 0)
            list.Children.Add(new TextBlock { Text = "Nothing copied yet", Opacity = 0.6, Margin = new Thickness(14, 10, 14, 10) });
        for (var i = 0; i < _items.Count; i++) list.Children.Add(Row(i));
        list.Children.Add(new TextBlock { Text = "1–9 or ↑↓ Enter to paste · Esc closes", FontSize = 10.5, Opacity = 0.45, Margin = new Thickness(10, 4, 10, 4) });

        var panel = new Border
        {
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x66, 255, 255, 255), Color.FromArgb(0x1C, 255, 255, 255), 45),
            Background = new LinearGradientBrush(Color.FromArgb(0xF2, 0x30, 0x30, 0x36), Color.FromArgb(0xF2, 0x1A, 0x1A, 0x1F), 60),
            Child = list,
            Margin = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = new ScaleTransform(0.94, 0.94),
            Opacity = 0,
        };
        Content = panel;
        Loaded += (_, _) =>
        {
            Highlight();
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            var scale = (ScaleTransform)panel.RenderTransform;
            var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
            Activate();
        };
        Deactivated += (_, _) => Finish(); // clicking elsewhere dismisses it

        // near the cursor, kept on screen
        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this);
        Left = cursor.X / dpi.DpiScaleX - 20;
        Top = cursor.Y / dpi.DpiScaleY + 14;
        SourceInitialized += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            if (Left + ActualWidth > area.Right) Left = area.Right - ActualWidth - 8;
            if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, cursor.Y / dpi.DpiScaleY - ActualHeight - 14);
        };
        PreviewKeyDown += OnKey;
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        Close();
    }

    private Border Row(int index)
    {
        var item = _items[index];
        var number = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            Child = new TextBlock { Text = (index + 1).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        FrameworkElement preview;
        if (item.Kind == ClipKind.Image && _history.LoadImage(item, 96) is { } thumb)
            preview = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Width = 44, Height = 28, Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill } };
        else
        {
            var text = item.Kind == ClipKind.Files ? string.Join(", ", item.Text.Split('\n').Select(System.IO.Path.GetFileName)) : item.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            preview = new TextBlock { Text = text.Length > 52 ? text[..52] + "…" : text, FontSize = 12.5, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center };
        }
        var row = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 6, 14, 6),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { number, preview } },
        };
        var i = index;
        row.MouseEnter += (_, _) => { _selected = i; Highlight(); };
        row.MouseLeftButtonUp += (_, _) => Choose(i);
        _rows.Add(row);
        return row;
    }

    private void Highlight()
    {
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Background = i == _selected
                ? new LinearGradientBrush(Color.FromArgb(0x66, 0x8B, 0x6C, 0xFF), Color.FromArgb(0x55, 0x2D, 0xD4, 0xFF), 20)
                : Brushes.Transparent;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Finish(); return; }
        if (e.Key == Key.Down) { _selected = Math.Min(_items.Count - 1, _selected + 1); Highlight(); }
        else if (e.Key == Key.Up) { _selected = Math.Max(0, _selected - 1); Highlight(); }
        else if (e.Key is Key.Enter or Key.Return) Choose(_selected);
        else if (e.Key >= Key.D1 && e.Key <= Key.D9 && e.Key - Key.D1 < _items.Count) Choose(e.Key - Key.D1);
        else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 && e.Key - Key.NumPad1 < _items.Count) Choose(e.Key - Key.NumPad1);
        else return;
        e.Handled = true;
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var item = _items[index];
        _history.Restore(item); // newest copy again, no duplicate
        Finish();
        if (_previous != IntPtr.Zero) SetForegroundWindow(_previous);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            const byte VK_CONTROL = 0x11, VK_V = 0x56;
            const uint KEYUP = 0x2;
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
        };
        timer.Start();
    }
}
