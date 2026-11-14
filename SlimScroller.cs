// Lumen (personal fork) - smooth, responsive scrolling with our own slim scroll bar (no Windows default bar): the wheel glides
// to its target instead of jumping, and a thin glass thumb shows where you are and can be dragged.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DIsland;

public sealed class SlimScroller
{
    private readonly ScrollViewer _sv;
    private readonly Border _thumb;
    private readonly Grid _bar;
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private double _target;
    private bool _animating;
    private DateTime _last;
    private bool _dragging;
    private double _dragGrab;

    public FrameworkElement Root { get; }

    public SlimScroller(ScrollViewer sv)
    {
        _sv = sv;
        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

        _thumb = new Border
        {
            Width = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 2, 0),
            Opacity = 0, IsHitTestVisible = false,
        };
        // a wider invisible strip makes the thin thumb easy to hover and grab
        _bar = new Grid { Width = 12, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.Arrow, Visibility = Visibility.Collapsed };
        _bar.Children.Add(_thumb);

        var root = new Grid();
        root.Children.Add(sv);
        root.Children.Add(_bar);
        Root = root;

        sv.ScrollChanged += (_, _) =>
        {
            if (!_animating && !_dragging) _target = sv.VerticalOffset; // moved by something else (new message, scroll to end)
            UpdateThumb();
            Reveal();
        };
        sv.SizeChanged += (_, _) => UpdateThumb();
        sv.MouseEnter += (_, _) => Reveal();
        _hide.Tick += (_, _) => { _hide.Stop(); if (!_dragging && !_bar.IsMouseOver) FadeThumb(0); };

        _bar.MouseEnter += (_, _) => { _thumb.Width = 7; FadeThumb(1); };
        _bar.MouseLeave += (_, _) => { if (!_dragging) { _thumb.Width = 4; Reveal(); } };
        _bar.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var y = e.GetPosition(_bar).Y;
            var top = _thumb.Margin.Top;
            _dragGrab = y >= top && y <= top + _thumb.Height ? y - top : _thumb.Height / 2; // clicking the track centres the thumb
            _dragging = true;
            _bar.CaptureMouse();
            DragTo(y);
        };
        _bar.MouseMove += (_, e) => { if (_dragging) DragTo(e.GetPosition(_bar).Y); };
        _bar.MouseLeftButtonUp += (_, _) => { _dragging = false; _bar.ReleaseMouseCapture(); if (!_bar.IsMouseOver) { _thumb.Width = 4; Reveal(); } };
    }

    private void DragTo(double y)
    {
        var track = Math.Max(1, _bar.ActualHeight - _thumb.Height);
        var ratio = Math.Clamp((y - _dragGrab) / track, 0, 1);
        _target = ratio * _sv.ScrollableHeight;
        _sv.ScrollToVerticalOffset(_target);
    }

    private void UpdateThumb()
    {
        var scrollable = _sv.ScrollableHeight;
        if (scrollable <= 0.5 || _sv.ActualHeight <= 0) { _bar.Visibility = Visibility.Collapsed; return; }
        _bar.Visibility = Visibility.Visible;
        var trackH = _sv.ActualHeight - 8;
        var h = Math.Clamp(_sv.ViewportHeight / _sv.ExtentHeight * trackH, 28, trackH);
        _thumb.Height = h;
        var top = 4 + (trackH - h) * (_sv.VerticalOffset / scrollable);
        _thumb.Margin = new Thickness(0, top, 2, 0);
    }

    private void Reveal()
    {
        if (_bar.Visibility != Visibility.Visible) return;
        FadeThumb(1);
        _hide.Stop();
        _hide.Start();
    }

    private void FadeThumb(double to) =>
        _thumb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 120 : 380)));

    /// <summary>Wheel input: glides to the new position. Returns false when there is nothing to scroll.</summary>
    public bool Wheel(int delta)
    {
        if (_sv.ScrollableHeight <= 0.5) return false;
        var from = _animating ? _target : _sv.VerticalOffset;
        _target = Math.Clamp(from - delta * 1.15, 0, _sv.ScrollableHeight);
        if (!_animating)
        {
            _animating = true;
            _last = DateTime.UtcNow;
            CompositionTarget.Rendering += Frame;
        }
        return true;
    }

    private void Frame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.05);
        _last = now;
        var offset = _sv.VerticalOffset;
        var next = offset + (_target - offset) * (1 - Math.Exp(-dt * 15)); // exponential glide: quick to start, soft to stop
        if (Math.Abs(_target - next) < 0.4) { next = _target; _animating = false; CompositionTarget.Rendering -= Frame; }
        _sv.ScrollToVerticalOffset(next);
    }
}

