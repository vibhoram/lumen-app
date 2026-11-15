// Lumen (personal fork) - Home glance card: fills the empty right side when nothing is playing.

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private string? _homeGlanceKey;
    private WeatherScene? _weatherScene;

    private void BuildHomeGlance()
    {
        if (HomeGlanceBody is null || HomeGlanceCard is null) return;
        if (ClockSection.Visibility != Visibility.Visible)
        {
            HomeGlanceCard.Visibility = Visibility.Collapsed;
            _homeGlanceKey = null;
            return;
        }

        var kind = _settings.HomeGlance;
        var secondary = (Brush)FindResource("Secondary");
        // Weather glance is a living sky card; keep it from crushing the clock.
        HomeGlanceCard.MinWidth = kind == HomeGlance.Weather ? 168 : 148;
        HomeGlanceCard.MaxWidth = kind == HomeGlance.Weather ? 210 : 200;
        HomeGlanceCard.Padding = kind == HomeGlance.Weather ? new Thickness(0) : new Thickness(12, 10, 12, 10);
        HomeGlanceCard.ClipToBounds = true;

        // Peek the key before rebuilding so the living weather scene isn't torn down every tick.
        var peek = PeekHomeGlanceKey(kind);
        if (peek == _homeGlanceKey && HomeGlanceBody.Children.Count > 0)
        {
            if (kind == HomeGlance.Weather && _weatherScene is not null)
            {
                if (_weather.Current is { } live)
                    _weatherScene.Show(live.Kind, live.IsDay, _settings.WeatherMotion);
                else
                    _weatherScene.Show(WeatherKind.Cloudy, true, _settings.WeatherMotion);
                _weatherScene.EnsureRunning();
            }
            HomeGlanceCard.Visibility = Visibility.Visible;
            return;
        }

        HomeGlanceBody.Children.Clear();
        // Non-weather glances use the glass fill; weather paints its own sky full-bleed.
        if (kind != HomeGlance.Weather)
        {
            HomeGlanceCard.Background = (Brush)FindResource("GlassFill");
            HomeGlanceCard.BorderBrush = (Brush)FindResource("GlassRim");
        }

        void Eyebrow(Panel host, string text, Brush? tint = null) =>
            host.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = tint ?? secondary,
                Margin = new Thickness(0, 0, 0, 4),
            });

        void Title(Panel host, string text, double size = 18) =>
            host.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });

        void Sub(Panel host, string text) =>
            host.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });

        string key;
        switch (kind)
        {
            case HomeGlance.Weather:
            {
                // Whole card = living sky; copy sits on a left scrim (Apple Weather vibe).
                _weatherScene ??= new WeatherScene();
                if (_weatherScene.Parent is Panel oldPanel) oldPanel.Children.Remove(_weatherScene);
                else if (_weatherScene.Parent is Decorator oldDec) oldDec.Child = null;

                var w = _weather.Current;
                var motion = _settings.WeatherMotion;
                if (w is null)
                {
                    _weatherScene.Show(WeatherKind.Cloudy, true, motion);
                    key = $"weather|empty|{motion}";
                }
                else
                {
                    _weatherScene.Show(w.Kind, w.IsDay, motion);
                    key = $"weather|{w.Temperature:0}|{w.Condition}|{w.IsDay}|{motion}";
                }

                var host = new Grid { Width = 196, Height = 86 };
                host.Children.Add(_weatherScene);
                // Layout may finish after Show — kick the timer once the card is actually on screen.
                host.Loaded += (_, _) => _weatherScene.EnsureRunning();
                var copy = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(14, 8, 14, 8),
                };
                Eyebrow(copy, "Weather", new SolidColorBrush(Color.FromArgb(0xEE, 0x9E, 0xE0, 0xFF)));
                if (w is null)
                {
                    Title(copy, "—", 24);
                    Sub(copy, WantsWeather ? (_weather.Problem ?? "Getting forecast…") : "Turn weather on");
                }
                else
                {
                    Title(copy, $"{w.Temperature:0}°{(w.Fahrenheit ? "F" : "C")}", 26);
                    Sub(copy, w.Condition);
                }
                // Readable over bright skies.
                foreach (TextBlock tb in copy.Children.OfType<TextBlock>())
                {
                    tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 6, ShadowDepth = 0, Opacity = 0.65, Color = Colors.Black,
                    };
                    if (tb.FontSize >= 20) tb.Foreground = Brushes.White;
                }
                host.Children.Add(copy);
                // Clip the sky to the card's rounded corners (ClipToBounds alone keeps a square).
                host.Clip = new RectangleGeometry(new Rect(0, 0, 196, 86), 16, 16);
                HomeGlanceBody.Children.Add(host);
                HomeGlanceCard.Background = Brushes.Transparent;
                HomeGlanceCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                HomeGlanceCard.ToolTip = _settings.WeatherEnabled ? "Open Weather" : "Weather (home glance)";
                break;
            }
            case HomeGlance.NextEvent:
            {
                Eyebrow(HomeGlanceBody, "Up next", (Brush)FindResource("Orange"));
                var now = DateTime.Now;
                var next = _calendar.Items
                    .Where(i => !i.AllDay && i.End > now)
                    .OrderBy(i => i.IsNow(now) ? 0 : 1)
                    .ThenBy(i => i.Start)
                    .FirstOrDefault();
                if (next is null)
                {
                    Title(HomeGlanceBody, "Free", 20);
                    Sub(HomeGlanceBody, _settings.CalendarEnabled ? "Nothing upcoming" : "Calendar is off");
                    key = "event|none";
                }
                else
                {
                    var when = next.IsNow(now) ? "Now" : next.Start.Date == now.Date ? next.Start.ToString("h:mm tt") : next.Start.ToString("ddd h:mm tt");
                    Title(HomeGlanceBody, when, 20);
                    Sub(HomeGlanceBody, next.Title);
                    key = $"event|{next.Title}|{next.Start:O}";
                }
                HomeGlanceCard.ToolTip = "Open Calendar";
                break;
            }
            case HomeGlance.Vitals:
            {
                Eyebrow(HomeGlanceBody, "System", (Brush)FindResource("Violet"));
                var (ram, _, _) = VitalsService.Ram();
                Title(HomeGlanceBody, $"{ram:0}%", 26);
                Sub(HomeGlanceBody, "RAM in use");
                key = $"vitals|{ram:0}";
                HomeGlanceCard.ToolTip = "Open System";
                break;
            }
            case HomeGlance.Todos:
            {
                Eyebrow(HomeGlanceBody, "To-do", (Brush)FindResource("Green"));
                var open = _notes.OpenCount;
                var urgent = _notes.Todos.Count(t => !t.Done && t.Priority == 2);
                Title(HomeGlanceBody, open == 0 ? "Clear" : open.ToString(), 26);
                Sub(HomeGlanceBody, open == 0 ? "Nothing open" : urgent > 0 ? $"{urgent} urgent · {open} open" : $"{open} open");
                key = $"todos|{open}|{urgent}";
                HomeGlanceCard.ToolTip = "Open To-Do";
                break;
            }
            case HomeGlance.Focus:
            {
                Eyebrow(HomeGlanceBody, "Focus", (Brush)FindResource("Green"));
                if (_focusSession.Phase != FocusPhase.Idle)
                {
                    Title(HomeGlanceBody, TimerService.Format(_focusSession.Remaining), 22);
                    Sub(HomeGlanceBody, $"{_focusSession.Phase} · {_focusSession.TodaySessions} today");
                    key = $"focus|run|{_focusSession.Remaining.TotalSeconds:0}";
                }
                else
                {
                    Title(HomeGlanceBody, _focusSession.TodaySessions == 0 ? "Ready" : $"{_focusSession.TodaySessions}", 22);
                    Sub(HomeGlanceBody, _focusSession.TodaySessions == 0 ? "No sessions yet" : $"{_focusSession.TodayMinutes} min today");
                    key = $"focus|idle|{_focusSession.TodaySessions}";
                }
                HomeGlanceCard.ToolTip = "Open Focus";
                break;
            }
            case HomeGlance.NextAlarm:
            {
                Eyebrow(HomeGlanceBody, "Alarm", (Brush)FindResource("Orange"));
                var now = DateTime.Now;
                DateTime? next = null;
                foreach (var alarm in _settings.Alarms.Where(a => a.Enabled))
                {
                    var at = now.Date + alarm.TimeOfDay;
                    if (at <= now) at = at.AddDays(1);
                    if (next is null || at < next) next = at;
                }
                if (next is null)
                {
                    Title(HomeGlanceBody, "None", 20);
                    Sub(HomeGlanceBody, "No alarms set");
                    key = "alarm|none";
                }
                else
                {
                    Title(HomeGlanceBody, next.Value.ToString("h:mm tt"), 20);
                    Sub(HomeGlanceBody, next.Value.Date == now.Date ? "Today" : "Tomorrow");
                    key = $"alarm|{next:O}";
                }
                HomeGlanceCard.ToolTip = "Jump to Alarm";
                break;
            }
            default: // Clipboard
            {
                Eyebrow(HomeGlanceBody, "Clipboard", secondary);
                var item = _clipHistory.Items.FirstOrDefault();
                if (item is null)
                {
                    Title(HomeGlanceBody, "Empty", 18);
                    Sub(HomeGlanceBody, "Copy something");
                    key = "clip|none";
                }
                else if (item.Kind == ClipKind.Image)
                {
                    Title(HomeGlanceBody, "Image", 18);
                    Sub(HomeGlanceBody, item.Time.ToLocalTime().ToString("h:mm tt"));
                    key = $"clip|img|{item.Id}";
                }
                else
                {
                    var text = (item.Text ?? "").Replace('\n', ' ').Trim();
                    Title(HomeGlanceBody, text.Length == 0 ? "Text" : (text.Length > 28 ? text[..28] + "…" : text), 15);
                    Sub(HomeGlanceBody, item.Time.ToLocalTime().ToString("h:mm tt"));
                    key = $"clip|{item.Id}";
                }
                HomeGlanceCard.ToolTip = "Open Clipboard";
                break;
            }
        }

        HomeGlanceCard.Visibility = Visibility.Visible;
        if (key != _homeGlanceKey)
        {
            _homeGlanceKey = key;
            var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 },
            };
            HomeGlanceScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            HomeGlanceScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }

    private string PeekHomeGlanceKey(HomeGlance kind) => kind switch
    {
        HomeGlance.Weather when _weather.Current is { } w => $"weather|{w.Temperature:0}|{w.Condition}|{w.IsDay}|{_settings.WeatherMotion}",
        HomeGlance.Weather => $"weather|empty|{_settings.WeatherMotion}",
        HomeGlance.NextEvent => PeekNextEventKey(),
        HomeGlance.Vitals => $"vitals|{VitalsService.Ram().Percent:0}",
        HomeGlance.Todos => PeekTodosKey(),
        HomeGlance.Focus when _focusSession.Phase != FocusPhase.Idle => $"focus|run|{_focusSession.Remaining.TotalSeconds:0}",
        HomeGlance.Focus => $"focus|idle|{_focusSession.TodaySessions}",
        HomeGlance.NextAlarm => PeekAlarmKey(),
        _ => PeekClipboardKey(),
    };

    private string PeekNextEventKey()
    {
        var now = DateTime.Now;
        var next = _calendar.Items.Where(i => !i.AllDay && i.End > now).OrderBy(i => i.IsNow(now) ? 0 : 1).ThenBy(i => i.Start).FirstOrDefault();
        return next is null ? "event|none" : $"event|{next.Title}|{next.Start:O}";
    }

    private string PeekTodosKey()
    {
        var open = _notes.OpenCount;
        var urgent = _notes.Todos.Count(t => !t.Done && t.Priority == 2);
        return $"todos|{open}|{urgent}";
    }

    private string PeekAlarmKey()
    {
        var now = DateTime.Now;
        DateTime? next = null;
        foreach (var alarm in _settings.Alarms.Where(a => a.Enabled))
        {
            var at = now.Date + alarm.TimeOfDay;
            if (at <= now) at = at.AddDays(1);
            if (next is null || at < next) next = at;
        }
        return next is null ? "alarm|none" : $"alarm|{next:O}";
    }

    private string PeekClipboardKey()
    {
        var item = _clipHistory.Items.FirstOrDefault();
        if (item is null) return "clip|none";
        if (item.Kind == ClipKind.Image) return $"clip|img|{item.Id}";
        return $"clip|{item.Id}";
    }

    private void OnHomeGlanceClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        switch (_settings.HomeGlance)
        {
            case HomeGlance.Weather:
                // Page is optional — glance works with "weather next to the clock" alone.
                if (_settings.WeatherEnabled) ShowPage(ExpandedPage.Weather, animate: true);
                else if (!WantsWeather) OpenSettings("Weather");
                break;
            case HomeGlance.NextEvent:
                if (_settings.CalendarEnabled) ShowPage(ExpandedPage.Calendar, animate: true);
                else OpenSettings("Calendar");
                break;
            case HomeGlance.Vitals:
                if (_settings.VitalsEnabled) ShowPage(ExpandedPage.Vitals, animate: true);
                else OpenSettings("System");
                break;
            case HomeGlance.Todos:
                if (_settings.TodoEnabled) ShowPage(ExpandedPage.Todo, animate: true);
                else OpenSettings("To-Do & Notes");
                break;
            case HomeGlance.Focus:
                if (_settings.FocusEnabled) ShowPage(ExpandedPage.Focus, animate: true);
                else OpenSettings("Focus");
                break;
            case HomeGlance.NextAlarm:
                ShowPage(ExpandedPage.Home, animate: true);
                SetClockMode(ClockMode.Alarm);
                break;
            case HomeGlance.Clipboard:
                if (_settings.ClipboardHistoryEnabled && _settings.ClipboardPageEnabled)
                    ShowPage(ExpandedPage.Clipboard, animate: true);
                else OpenSettings("Clipboard");
                break;
        }
    }
}
