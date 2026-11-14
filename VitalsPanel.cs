// Lumen (personal fork) - the Vitals page: RAM / CPU / GPU / network rings, the apps using the most memory,
// and a three-strength "free RAM" button. Updates only while it is on screen.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>A ring gauge: a faint track and a coloured arc that changes colour as the value climbs.</summary>
internal sealed class RingGauge : FrameworkElement
{
    private double _value, _shown;
    private readonly DispatcherTimer _anim = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };

    public RingGauge()
    {
        Width = Height = 62;
        _anim.Tick += (_, _) =>
        {
            _shown += (_value - _shown) * 0.2;
            if (Math.Abs(_value - _shown) < 0.2) { _shown = _value; _anim.Stop(); }
            InvalidateVisual();
        };
    }

    public bool Unavailable { get; set; }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); if (!_anim.IsEnabled) _anim.Start(); }
    }

    public static Color ColorFor(double percent) =>
        percent < 65 ? Color.FromRgb(0x30, 0xD1, 0x58) : percent < 85 ? Color.FromRgb(0xFF, 0xD6, 0x0A) : Color.FromRgb(0xFF, 0x45, 0x3A);

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(Width / 2, Height / 2);
        var r = Width / 2 - 5;
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), 5), c, r, r);
        if (Unavailable) return;
        var sweep = Math.Max(0.5, _shown / 100 * 359.5);
        var start = -90 * Math.PI / 180;
        var end = (-90 + sweep) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start));
        var p1 = new Point(c.X + r * Math.Cos(end), c.Y + r * Math.Sin(end));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        var color = ColorFor(_shown);
        var glow = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
        dc.DrawGeometry(null, new Pen(glow, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }
}

public sealed class VitalsPanel : Grid
{
    private readonly VitalsService _service = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
    private readonly AppSettings _settings;

    private readonly RingGauge _ram = new(), _cpu = new(), _gpu = new(), _net = new();
    private readonly TextBlock _ramText = Number(), _cpuText = Number(), _gpuText = Number(), _netText = Number();
    private readonly TextBlock _ramSub = Sub(), _cpuSub = Sub(), _gpuSub = Sub(), _netSub = Sub();
    private readonly StackPanel _apps = new();
    private readonly TextBlock _result = new() { FontSize = 11.5, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<Border> _cleanButtons = new();
    private bool _busy;
    private bool _sampling;

    /// <summary>Raised after a clean: the pet can react.</summary>
    public event Action<CleanResult>? Cleaned;

    /// <summary>Raised when the page changed height (the island must resize to fit).</summary>
    public event Action? HeightChanged;
    private int _lastRows = -1;

    public VitalsPanel(AppSettings settings)
    {
        _settings = settings;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Background = Brushes.Transparent;

        var header = new Grid { Margin = new Thickness(2, 0, 2, 8) };
        header.Children.Add(new TextBlock { Text = "This PC", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = "live", FontSize = 11, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(header);

        var rings = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
        rings.Children.Add(Cell(_ram, _ramText, "RAM", _ramSub));
        rings.Children.Add(Cell(_cpu, _cpuText, "CPU", _cpuSub));
        rings.Children.Add(Cell(_gpu, _gpuText, "GPU", _gpuSub));
        rings.Children.Add(Cell(_net, _netText, "NET", _netSub));
        SetRow(rings, 1);
        Children.Add(rings);

        var appsBox = new Border
        {
            Background = (Brush)Application.Current.FindResource("GlassFill"),
            BorderBrush = (Brush)Application.Current.FindResource("GlassRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Using the most memory", FontSize = 11, Opacity = 0.55, Margin = new Thickness(0, 0, 0, 4) },
                    _apps,
                },
            },
        };
        SetRow(appsBox, 2);
        Children.Add(appsBox);

        var actions = new Grid { Margin = new Thickness(0, 10, 0, 2) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(CleanButton("Tidy", "Trims the memory your apps aren't using right now. Safe, nothing closes.", RamCleanMode.Normal));
        buttons.Children.Add(CleanButton("Deeper", "Also trims background and system apps and cached data.", RamCleanMode.More));
        buttons.Children.Add(CleanButton("Maximum", "Everything above plus the standby cache (needs administrator rights).", RamCleanMode.Maximum));
        actions.Children.Add(buttons);
        SetColumn(_result, 1);
        _result.Margin = new Thickness(10, 0, 0, 0);
        _result.Text = "Free RAM, nothing closes";
        actions.Children.Add(_result);
        SetRow(actions, 3);
        Children.Add(actions);

        _timer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) { _timer.Start(); await RefreshAsync(); }
            else _timer.Stop();
        };
    }

    private static TextBlock Number() => new() { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Sub() => new() { FontSize = 10.5, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92 };

    private static FrameworkElement Cell(RingGauge ring, TextBlock number, string label, TextBlock sub)
    {
        var holder = new Grid { Width = 62, Height = 62, HorizontalAlignment = HorizontalAlignment.Center };
        holder.Children.Add(ring);
        holder.Children.Add(number);
        return new StackPanel
        {
            Children =
            {
                holder,
                new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                sub,
            },
        };
    }

    private Border CleanButton(string text, string tip, RamCleanMode mode)
    {
        var button = new Border
        {
            Background = (Brush)Application.Current.FindResource("GlassFill"),
            BorderBrush = (Brush)Application.Current.FindResource("GlassRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 6, 11, 6),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
        button.MouseLeftButtonUp += async (_, e) => { e.Handled = true; await CleanAsync(mode); };
        _cleanButtons.Add(button);
        return button;
    }

    private async Task CleanAsync(RamCleanMode mode)
    {
        if (_busy) return;
        _busy = true;
        foreach (var b in _cleanButtons) b.Opacity = 0.5;
        _result.Text = "Tidying…";
        var result = await VitalsService.CleanAsync(mode);
        _result.Text = (result.FreedGb >= 0.05 ? $"Freed {result.FreedGb:0.0} GB" : "Already lean") + " · " + result.Note;
        foreach (var b in _cleanButtons) b.Opacity = 1;
        _busy = false;
        Cleaned?.Invoke(result);
        await RefreshAsync();
    }

    private static string Rate(double bytesPerSecond)
    {
        var bits = bytesPerSecond;
        if (bits >= 1_048_576) return $"{bits / 1_048_576:0.0} MB/s";
        if (bits >= 1024) return $"{bits / 1024:0} KB/s";
        return $"{bits:0} B/s";
    }

    private async Task RefreshAsync()
    {
        if (_sampling || !IsVisible) return;
        _sampling = true;
        try
        {
            var (snapshot, apps) = await Task.Run(() => (_service.Sample(), VitalsService.TopApps(5)));
            _ram.Value = snapshot.RamPercent;
            _ramText.Text = $"{snapshot.RamPercent:0}%";
            _ramSub.Text = $"{snapshot.RamUsedGb:0.0} / {snapshot.RamTotalGb:0} GB";
            _cpu.Value = snapshot.CpuPercent;
            _cpuText.Text = $"{snapshot.CpuPercent:0}%";
            _cpuSub.Text = "Processor";
            if (snapshot.GpuPercent is { } gpu)
            {
                _gpu.Unavailable = false;
                _gpu.Value = gpu;
                _gpuText.Text = $"{gpu:0}%";
                _gpuSub.Text = "Graphics";
            }
            else
            {
                _gpu.Unavailable = _gpuText.Text.Length == 0 ? false : _gpu.Unavailable;
                _gpuText.Text = "—";
                _gpuSub.Text = "measuring…";
            }
            var busiest = Math.Max(snapshot.NetDownBytes, snapshot.NetUpBytes);
            _net.Value = Math.Min(100, busiest / 12_500_000 * 100); // 100 Mbit/s fills the ring
            _netText.Text = busiest >= 1_048_576 ? $"{busiest / 1_048_576:0.#}" : $"{busiest / 1024:0}";
            _netSub.Text = $"↓{Rate(snapshot.NetDownBytes)} ↑{Rate(snapshot.NetUpBytes)}";
            if (snapshot.RefreshHz > 0) _gpuSub.Text += $" · {snapshot.RefreshHz} Hz";
            BuildApps(apps, snapshot.RamTotalGb);
            if (apps.Count != _lastRows) { _lastRows = apps.Count; HeightChanged?.Invoke(); }
        }
        catch (Exception) { /* a missed sample is fine */ }
        finally { _sampling = false; }
    }

    private void BuildApps(List<TopApp> apps, double totalGb)
    {
        _apps.Children.Clear();
        var max = apps.Count == 0 ? 1 : apps.Max(a => a.GigaBytes);
        foreach (var app in apps)
        {
            var share = Math.Clamp(app.GigaBytes / Math.Max(0.1, totalGb) * 100, 0, 100);
            var color = RingGauge.ColorFor(share * 3.2); // 25% of RAM in one app reads as red
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.Children.Add(new TextBlock
            {
                Text = app.Count > 1 ? $"{app.Name}  ×{app.Count}" : app.Name,
                FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var track = new Grid { Height = 5, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) });
            var fill = new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, e) => fill.Width = Math.Max(3, e.NewSize.Width * app.GigaBytes / max);
            track.Children.Add(fill);
            SetColumn(track, 1);
            row.Children.Add(track);
            var amount = new TextBlock { Text = app.GigaBytes >= 1 ? $"{app.GigaBytes:0.0} GB" : $"{app.GigaBytes * 1024:0} MB", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            SetColumn(amount, 2);
            row.Children.Add(amount);
            _apps.Children.Add(row);
        }
    }
}
