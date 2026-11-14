// Lumen (personal fork) - the Vitals page plumbing and the "RAM is nearly full" alert.

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private VitalsPanel _vitals = null!;
    private readonly DispatcherTimer _ramWatch = new() { Interval = TimeSpan.FromSeconds(6) };
    private int _ramHighTicks;
    private DateTime _lastRamAlert = DateTime.MinValue;

    private void WireVitals()
    {
        _vitals = new VitalsPanel(_settings);
        VitalsPage.Children.Add(_vitals);
        _vitals.Cleaned += result =>
        {
            if (result.FreedGb >= 0.3) _pet.React(PetEvent.RamFreed, $"{result.FreedGb:0.0} GB");
        };
        _vitals.HeightChanged += () => { if (_state.IsExpanded && _page == ExpandedPage.Vitals) Render(); };
        _ramWatch.Tick += (_, _) => { CheckRam(); UpdateTrayTooltip(); _clipHistory.PurgeExpired(); };
        _ramWatch.Start();
    }

    /// <summary>Cheap check (one API call): two readings in a row over the limit raise a single alert, then 10 quiet minutes.</summary>
    private void CheckRam()
    {
        if (!_settings.VitalsAlertEnabled || IslandHidden) { _ramHighTicks = 0; return; }
        var (percent, _, _) = VitalsService.Ram();
        _ramHighTicks = percent >= _settings.VitalsAlertPercent ? _ramHighTicks + 1 : 0;
        if (_ramHighTicks < 2 || (DateTime.Now - _lastRamAlert).TotalMinutes < 10) return;
        _lastRamAlert = DateTime.Now;
        var percentText = $"{percent:0}%";
        _ = Task.Run(VitalsService.BiggestApp).ContinueWith(t => Dispatcher.InvokeAsync(() =>
        {
            var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
            ShowInfo(TransientPriority.LowBattery, 340, "", $"RAM {percentText} full", t.Result is { Length: > 0 } big ? big : "open Vitals to free some",
                detailBrush: red, glyphBrush: red, seconds: 5);
            _pet.React(PetEvent.RamHigh);
        }));
    }

    /// <summary>Set by the app so the tray icon's hover text can show live status.</summary>
    public Action<string>? TrayTooltipChanged { get; set; }

    private void UpdateTrayTooltip()
    {
        if (TrayTooltipChanged is null) return;
        var (percent, _, _) = VitalsService.Ram();
        var parts = new List<string> { $"Lumen · RAM {percent:0}%" };
        if (_timer.IsActive) parts.Add($"timer {TimerService.Format(_timer.Remaining)}");
        if (_nextAlarmAt is { } at && (at - DateTime.Now).TotalHours <= 24) parts.Add($"alarm {at:h:mm tt}");
        TrayTooltipChanged(string.Join(" · ", parts));
    }
}
