// Lumen (personal fork) - an iOS-style drum (wheel) picker: a short column of values you scroll with the mouse wheel,
// drag with the mouse, or click above/below. It rolls with momentum and settles on a value with a small spring.

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DIsland;

public sealed class DrumPicker : FrameworkElement
{
    private const double ItemHeight = 25;
    private const int Visible = 3;

    private IReadOnlyList<string> _items = Array.Empty<string>();
    private double _pos;            // continuous position, in items (the item at the centre)
    private double _velocity;       // items per second
    private double _target;         // the item it is settling on
    private bool _dragging;
    private double _dragStartY, _dragStartPos, _lastY, _lastT, _dragSpeed;
    private bool _animating;
    private DateTime _last;
    private int _reported = -1;

    private readonly Typeface _face = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public DrumPicker(double width = 46)
    {
        Width = width;
        Height = ItemHeight * Visible;
        Cursor = Cursors.SizeNS;
        Tag = "WheelOwner"; // the island must not flip pages while the wheel turns this
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
    }

    public Brush? Background { get; set; }

    /// <summary>Wrap around (hours, minutes) or stop at the ends (AM/PM).</summary>
    public bool Loop { get; set; } = true;

    public event Action<int>? ValueChanged;

    public IReadOnlyList<string> Items
    {
        get => _items;
        set { _items = value; _pos = _target = Math.Clamp(_pos, 0, Math.Max(0, value.Count - 1)); InvalidateVisual(); }
    }

    public int Index
    {
        get => Wrap((int)Math.Round(_target));
        set
        {
            if (_items.Count == 0) return;
            value = Wrap(value);
            if (!Loop) { _pos = _target = value; }
            else
            {
                // take the shortest way round
                var cur = (int)Math.Round(_target);
                var delta = value - Wrap(cur);
                if (delta > _items.Count / 2) delta -= _items.Count;
                if (delta < -_items.Count / 2) delta += _items.Count;
                _pos = _target = cur + delta;
            }
            _reported = value;
            InvalidateVisual();
        }
    }

    private int Wrap(int i) => _items.Count == 0 ? 0 : Loop ? ((i % _items.Count) + _items.Count) % _items.Count : Math.Clamp(i, 0, _items.Count - 1);

    private void Report()
    {
        var i = Wrap((int)Math.Round(_target));
        if (i == _reported) return;
        _reported = i;
        ValueChanged?.Invoke(i);
    }

    private void SettleOn(double to)
    {
        _target = Loop ? to : Math.Clamp(to, 0, Math.Max(0, _items.Count - 1));
        Report();
        if (_animating) return;
        _animating = true;
        _last = DateTime.UtcNow;
        CompositionTarget.Rendering += Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.04);
        _last = now;
        const double k = 300, c = 24;      // lightly under-damped: a tiny overshoot, then still
        for (var i = 0; i < 3; i++)
        {
            var h = dt / 3;
            _velocity += (k * (_target - _pos) - c * _velocity) * h;
            _pos += _velocity * h;
        }
        if (Math.Abs(_target - _pos) < 0.002 && Math.Abs(_velocity) < 0.02)
        {
            _pos = _target;
            _velocity = 0;
            _animating = false;
            CompositionTarget.Rendering -= Frame;
        }
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        SettleOn(Math.Round(_target) + (e.Delta > 0 ? -1 : 1));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        CaptureMouse();
        _dragging = true;
        _dragStartY = _lastY = e.GetPosition(this).Y;
        _dragStartPos = _pos;
        _lastT = Environment.TickCount64;
        _dragSpeed = 0;
        _velocity = 0;
        if (_animating) { CompositionTarget.Rendering -= Frame; _animating = false; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var y = e.GetPosition(this).Y;
        _pos = _dragStartPos - (y - _dragStartY) / ItemHeight;
        if (!Loop) _pos = Math.Clamp(_pos, -0.4, Math.Max(0, _items.Count - 1) + 0.4);
        var now = Environment.TickCount64;
        var dt = Math.Max(1, now - _lastT) / 1000.0;
        _dragSpeed = 0.6 * _dragSpeed + 0.4 * (-(y - _lastY) / ItemHeight / dt);
        _lastY = y;
        _lastT = now;
        _target = _pos;
        Report();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        _dragging = false;
        ReleaseMouseCapture();
        var moved = Math.Abs(e.GetPosition(this).Y - _dragStartY);
        if (moved < 4)
        {
            // a click: above the middle row goes up, below goes down
            var y = e.GetPosition(this).Y;
            var step = y < Height / 2 - ItemHeight / 2 ? -1 : y > Height / 2 + ItemHeight / 2 ? 1 : 0;
            SettleOn(Math.Round(_pos) + step);
            return;
        }
        _velocity = Math.Clamp(_dragSpeed, -14, 14);
        SettleOn(Math.Round(_pos + _velocity * 0.18)); // fling: coast a little further, then settle
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height)); // hit area
        if (_items.Count == 0) return;
        var centerY = Height / 2;
        // the highlight band behind the chosen row
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), null, new Rect(2, centerY - ItemHeight / 2 + 1, Width - 4, ItemHeight - 2), 9, 9);

        var first = (int)Math.Floor(_pos) - 2;
        for (var i = first; i <= first + 5; i++)
        {
            var offset = i - _pos;                       // distance from the centre, in rows
            var d = Math.Abs(offset);
            if (d > 1.9) continue;
            var index = Loop ? ((i % _items.Count) + _items.Count) % _items.Count : i;
            if (index < 0 || index >= _items.Count) continue;
            var alpha = (byte)Math.Clamp(255 * (1 - 0.62 * d) * (d > 1.4 ? (1.9 - d) / 0.5 : 1), 0, 255);
            var size = 19 - 4.5 * Math.Min(d, 1.2);
            var text = new FormattedText(_items[index], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, size,
                new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var y = centerY + offset * ItemHeight - text.Height / 2;
            // the rows lean away from the middle a little, like a real drum
            var scaleY = 1 - 0.12 * Math.Min(d, 1.5);
            dc.PushTransform(new ScaleTransform(1, scaleY, 0, centerY + offset * ItemHeight));
            dc.DrawText(text, new Point((Width - text.Width) / 2, y));
            dc.Pop();
        }
    }
}
