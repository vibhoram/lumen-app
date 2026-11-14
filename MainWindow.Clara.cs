// D-Island (personal fork) - Clara's link to the PC: what she's told about the moment, and the tools she can run.

using System.Text;
using System.Text.Json;
using System.Windows;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private void WireClara()
    {
        _assistant.ContextProvider = ClaraContext;
        _assistant.ToolRunner = RunClaraTool;
    }

    /// <summary>"Ask Clara" on a clipboard item: open her page with the item attached and the cursor ready.</summary>
    private async void AskClaraAbout(ClipItem item)
    {
        try
        {
            ShowPage(ExpandedPage.Assistant, animate: true);
            if (item.Kind == ClipKind.Image) _assistant.AttachImage(_clipHistory.LoadImage(item));
            else _assistant.AttachText(item.Text);
            await Task.Delay(260);
            BeginTyping(_assistant.Input);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    /// <summary>A few facts about right now. No names, places or account details: just time, music, battery, weather.</summary>
    private string ClaraContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Local time: {DateTime.Now:dddd, d MMMM yyyy, h:mm tt}.");
        try
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                sb.AppendLine($"Battery: {power.BatteryLifePercent:P0}" +
                              (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? ", plugged in." : "."));
        }
        catch (Exception) { }
        if (_media.Current is { } m)
            sb.AppendLine($"Media: \"{m.Title}\" by {m.Artist} is {(m.IsPlaying ? "playing" : "paused")} in {m.AppName}.");
        if (_timer.IsActive) sb.AppendLine("A timer is running on the island.");
        sb.AppendLine($"To-do list: {_notes.OpenCount} open, {_notes.DoneCount} done.");
        if (_weather.Current is { } w)
            sb.AppendLine($"Weather: {w.Temperature:0} degrees {(w.Fahrenheit ? "F" : "C")}, {w.Condition}.");
        return sb.ToString();
    }

    private async Task<(bool Ok, string Result, string Summary)> RunClaraTool(string name, string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            string Str(string key) => doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            switch (name)
            {
                case "set_timer":
                {
                    var minutes = doc.RootElement.TryGetProperty("minutes", out var v) && v.TryGetDouble(out var d) ? d : 0;
                    if (minutes <= 0 || minutes > 720) return (false, "Minutes must be between a few seconds and 12 hours.", "");
                    var span = TimeSpan.FromMinutes(minutes);
                    _timer.Start(span);
                    return (true, $"Timer started for {TimerService.Format(span)}.", $"Timer · {TimerService.Format(span)}");
                }
                case "set_reminder":
                {
                    var minutes = doc.RootElement.TryGetProperty("minutes", out var v) && v.TryGetDouble(out var d) ? d : 0;
                    var text = Str("text").Trim();
                    if (minutes < 1 || minutes > 1440) return (false, "Minutes must be between 1 and 1440.", "");
                    var at = DateTime.Now.AddMinutes(minutes);
                    _settings.Alarms.Add(new Alarm { Time = at.ToString("HH:mm"), Enabled = true, Daily = false, Label = text.Length > 0 ? text : "Reminder" });
                    _settings.Save();
                    _alarms.Reschedule();
                    return (true, $"Reminder set for {at:h:mm tt}.", $"Reminder · {Trim(text.Length > 0 ? text : "reminder", 22)} · {at:h:mm tt}");
                }
                case "quiet_mode":
                {
                    var minutes = doc.RootElement.TryGetProperty("minutes", out var v) && v.TryGetDouble(out var d) ? d : 0;
                    if (minutes <= 0) { EndQuiet(); return (true, "Quiet mode is off.", "Quiet mode off"); }
                    StartQuiet(TimeSpan.FromMinutes(Math.Min(minutes, 720)));
                    return (true, $"Quiet for {minutes:0} minutes.", $"Quiet · {minutes:0} min");
                }
                case "add_todo":
                {
                    var text = Str("text").Trim();
                    if (text.Length == 0 || !_notes.Add(text)) return (false, "Couldn't add that to-do.", "");
                    return (true, "Added to the to-do list.", $"To-do · {Trim(text, 28)}");
                }
                case "append_note":
                {
                    var text = Str("text").Trim();
                    if (text.Length == 0) return (false, "Nothing to add.", "");
                    _notes.Note = (string.IsNullOrEmpty(_notes.Note) ? "" : _notes.Note.TrimEnd() + "\n") + text;
                    return (true, "Added to the note.", $"Note · {Trim(text, 28)}");
                }
                case "media_control":
                {
                    switch (Str("action"))
                    {
                        case "play_pause": _media.TogglePlayPause(); return (true, "Toggled play/pause.", "Play / pause");
                        case "next": _media.Next(); return (true, "Skipped to the next track.", "Next track");
                        case "previous": _media.Previous(); return (true, "Went to the previous track.", "Previous track");
                        default: return (false, "Unknown action; use play_pause, next or previous.", "");
                    }
                }
                case "open_app":
                {
                    var wanted = Str("name").Trim();
                    var apps = await QuickAppsService.ListAsync();
                    var app = apps.FirstOrDefault(a => a.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                              ?? apps.FirstOrDefault(a => a.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                              ?? apps.FirstOrDefault(a => a.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
                    if (app is null) return (false, $"No installed app matches \"{wanted}\".", $"Couldn't find {Trim(wanted, 20)}");
                    QuickAppsService.Launch(app.Id);
                    return (true, $"Opened {app.Name}.", $"Opened {app.Name}");
                }
                case "copy_to_clipboard":
                {
                    var text = Str("text");
                    if (text.Length == 0) return (false, "Nothing to copy.", "");
                    Clipboard.SetText(text);
                    return (true, "Copied to the clipboard.", "Copied to clipboard");
                }
                case "remember":
                {
                    var fact = Str("fact");
                    if (fact.Length == 0) return (false, "Nothing to remember.", "");
                    ClaraMemory.Shared.Add(fact);
                    return (true, "Remembered.", "Remembered");
                }
                case "forget":
                {
                    var n = ClaraMemory.Shared.Remove(Str("contains"));
                    return (n > 0, n > 0 ? $"Forgot {n} thing(s)." : "Nothing matched.", n > 0 ? "Forgotten" : "");
                }
                default:
                    return (false, $"Unknown tool {name}.", "");
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return (false, "That failed: " + ex.Message, $"{name} failed");
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
