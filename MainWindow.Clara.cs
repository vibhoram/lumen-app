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
        var nowLocal = DateTime.Now;
        var part = nowLocal.Hour switch { < 5 => "the middle of the night", < 12 => "morning", < 17 => "afternoon", < 21 => "evening", _ => "late evening" };
        sb.AppendLine($"Local time: {nowLocal:dddd, d MMMM yyyy, h:mm tt} ({part}). Week {System.Globalization.ISOWeek.GetWeekOfYear(nowLocal)} of the year.");
        if (ClaraTopics.LastChat is { } last)
        {
            var gap = nowLocal - last;
            sb.AppendLine($"Last time you two chatted: {(gap.TotalMinutes < 2 ? "moments ago" : gap.TotalHours < 1 ? $"{gap.TotalMinutes:0} min ago" : gap.TotalDays < 1 ? $"{gap.TotalHours:0} h ago" : $"{gap.TotalDays:0} days ago")}.");
        }
        var urgent = _notes.Todos.Where(t => !t.Done && t.Priority == 2).Select(t => t.Text).Take(4).ToList();
        if (urgent.Count > 0) sb.AppendLine("Urgent to-dos: " + string.Join("; ", urgent) + ".");
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
        var (ramPercent, _, _) = VitalsService.Ram();
        sb.AppendLine($"RAM use: {ramPercent:0}%.");
        var nextAlarm = _settings.Alarms.Where(a => a.Enabled).OrderBy(a => a.TimeOfDay).FirstOrDefault();
        if (nextAlarm is not null) sb.AppendLine($"Next alarm on the list: {nextAlarm.Time}{(string.IsNullOrEmpty(nextAlarm.Label) ? "" : " (" + nextAlarm.Label + ")")}.");
        sb.AppendLine($"To-do list: {_notes.OpenCount} open, {_notes.DoneCount} done.");
        if (_focusSession.Phase != FocusPhase.Idle)
            sb.AppendLine($"Focus session: {_focusSession.Phase} phase, {TimerService.Format(_focusSession.Remaining)} left, {_focusSession.TodaySessions} sessions done today.");
        else if (_focusSession.TodaySessions > 0)
            sb.AppendLine($"No focus session running; {_focusSession.TodaySessions} done today ({_focusSession.TodayMinutes} min).");
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
                case "start_focus":
                {
                    var minutes = doc.RootElement.TryGetProperty("minutes", out var v) && v.TryGetDouble(out var d) ? d : 25;
                    var m = (int)Math.Clamp(minutes, 5, 180);
                    _focusSession.StartFocusWithMinutes(m);
                    return (true, $"Focus session started for {m} minutes.", $"Focus · {m} min");
                }
                case "stop_focus":
                    _focusSession.Stop();
                    return (true, "Focus session stopped.", "Focus stopped");
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
                case "add_todos":
                {
                    if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return (false, "Give an items array.", "");
                    int added = 0, urgentCount = 0;
                    foreach (var it in arr.EnumerateArray())
                    {
                        var text = it.ValueKind == JsonValueKind.String ? it.GetString() ?? "" : it.TryGetProperty("text", out var tv) ? tv.GetString() ?? "" : "";
                        var urgencyText = it.ValueKind == JsonValueKind.Object && it.TryGetProperty("urgency", out var uv) ? uv.GetString() ?? "" : "";
                        var priority = ParseUrgency(urgencyText);
                        if (_notes.Add(text, priority)) { added++; if (priority == 2) urgentCount++; }
                    }
                    return (added > 0, added > 0 ? $"Added {added} to-dos ({urgentCount} urgent)." : "Nothing was added.", added > 0 ? $"Added {added} to-dos" : "");
                }
                case "set_todo_priority":
                {
                    var match = Str("text").Trim();
                    var priority = ParseUrgency(Str("urgency"));
                    var targets = _notes.Todos.Where(t => !t.Done && match.Length > 0 && t.Text.Contains(match, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (targets.Count == 0) return (false, "No open to-do matches that.", "");
                    foreach (var t in targets) _notes.SetPriority(t.Id, priority);
                    return (true, $"Set {targets.Count} to-do(s) to {(priority == 2 ? "urgent" : priority == 1 ? "high" : "normal")}.", $"Priority · {targets.Count}");
                }
                case "change_setting":
                {
                    var key = Str("name").Trim();
                    var value = Str("value").Trim();
                    if (!SettableNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                        return (false, "Unknown setting. Settable: " + string.Join(", ", SettableNames) + ".", "");
                    var property = typeof(AppSettings).GetProperty(SettableNames.First(n => n.Equals(key, StringComparison.OrdinalIgnoreCase)))!;
                    try
                    {
                        var type = property.PropertyType;
                        object parsed = type.IsEnum ? Enum.Parse(type, value, true)
                            : type == typeof(bool) ? bool.Parse(value)
                            : type == typeof(int) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                            : type == typeof(double) ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                            : value;
                        property.SetValue(_settings, parsed);
                    }
                    catch (Exception)
                    {
                        var hint = property.PropertyType.IsEnum ? " Options: " + string.Join(", ", Enum.GetNames(property.PropertyType)) + "." : "";
                        return (false, $"Couldn't use \"{value}\" for {property.Name}.{hint}", "");
                    }
                    _settings.NotifyChanged();
                    _settings.Save();
                    return (true, $"{property.Name} is now {value}.", $"{property.Name} · {value}");
                }
                case "describe_app":
                {
                    var lines = SettableNames.Select(n => { var pr = typeof(AppSettings).GetProperty(n)!; return $"{n} = {pr.GetValue(_settings)}" + (pr.PropertyType.IsEnum ? $" (options: {string.Join("/", Enum.GetNames(pr.PropertyType))})" : ""); });
                    return (true, "Current settings:\n" + string.Join("\n", lines) + $"\nPages: {string.Join(", ", AvailablePages().Select(PageTitle))}. Open to-dos: {_notes.OpenCount}.", "Checked the app");
                }
                case "get_notifications":
                {
                    var items = _notifications.Items.Take(8).ToList();
                    if (items.Count == 0) return (true, "There are no notifications right now.", "Checked notifications");
                    var lines = items.Select(n => $"{n.AppName} ({(DateTimeOffset.Now - n.Time).TotalMinutes:0} min ago): {n.Title} - {Trim(n.Body.Replace(Environment.NewLine, " "), 120)}");
                    return (true, string.Join(Environment.NewLine, lines), $"{items.Count} notifications");
                }
                case "get_clipboard":
                {
                    var items = _clipHistory.Items.Where(c => c.Kind is ClipKind.Text or ClipKind.Link).Take(5).ToList();
                    if (items.Count == 0) return (true, "The clipboard history has no text yet.", "Checked clipboard");
                    return (true, string.Join(Environment.NewLine + "---" + Environment.NewLine, items.Select(c => Trim(c.Text, 500))), $"{items.Count} clipboard items");
                }
                case "apply_look":
                {
                    var wanted = Str("name").Trim();
                    var preset = LookPresets.BuiltIn.Concat(_settings.CustomPresets).FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                                 ?? LookPresets.BuiltIn.Concat(_settings.CustomPresets).FirstOrDefault(p => p.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
                    if (preset is null) return (false, "No look with that name. Available: " + string.Join(", ", LookPresets.BuiltIn.Concat(_settings.CustomPresets).Select(p => p.Name)) + ".", "");
                    LookPresets.Apply(_settings, preset);
                    _settings.NotifyChanged();
                    _settings.Save();
                    return (true, $"Applied the {preset.Name} look.", $"Look · {preset.Name}");
                }
                case "set_pet":
                {
                    if (!Enum.TryParse<PetKind>(Str("species"), true, out var kind)) return (false, "Species must be one of: " + string.Join(", ", Enum.GetNames<PetKind>()) + ".", "");
                    _settings.PetKind = kind;
                    _settings.PetEnabled = true;
                    _settings.NotifyChanged();
                    _settings.Save();
                    return (true, $"The pet is now a {kind}.", $"Pet · {kind}");
                }
                case "open_page":
                {
                    if (!Enum.TryParse<ExpandedPage>(Str("page"), true, out var page) || !AvailablePages().Contains(page)) return (false, "That page isn't available.", "");
                    ShowPage(page, animate: true);
                    return (true, $"Opened {PageTitle(page)}.", $"Opened {PageTitle(page)}");
                }
                case "list_todos":
                {
                    var items = _notes.Todos;
                    if (items.Count == 0) return (true, "The to-do list is empty.", "To-dos checked");
                    return (true, string.Join("\n", items.Select(t => (t.Done ? "[done] " : "[open] ") + t.Text)), $"{items.Count} to-dos");
                }
                case "complete_todo":
                case "delete_todo":
                {
                    var match = Str("text").Trim();
                    var all = Str("scope").Equals("all_done", StringComparison.OrdinalIgnoreCase);
                    var targets = _notes.Todos.Where(t => all ? t.Done : (match.Length > 0 && t.Text.Contains(match, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (targets.Count == 0) return (false, "No to-do matches that.", "");
                    foreach (var t in targets)
                    {
                        if (name == "complete_todo") _notes.SetDone(t.Id, true);
                        else _notes.Remove(t.Id);
                    }
                    var verb = name == "complete_todo" ? "Checked off" : "Deleted";
                    return (true, $"{verb} {targets.Count} to-do(s).", $"{verb} {targets.Count}");
                }
                case "set_alarm":
                {
                    var time = Str("time").Trim();
                    if (!TimeSpan.TryParse(time, out var tod) || tod < TimeSpan.Zero || tod >= TimeSpan.FromDays(1)) return (false, "Time must look like 07:30 (24-hour).", "");
                    var daily = doc.RootElement.TryGetProperty("daily", out var dv) && dv.ValueKind == JsonValueKind.True;
                    var label = Str("label").Trim();
                    _settings.Alarms.Add(new Alarm { Time = tod.ToString(@"hh\:mm"), Enabled = true, Daily = daily, Label = label.Length > 0 ? label : "Alarm" });
                    _settings.Save();
                    _alarms.Reschedule();
                    return (true, $"Alarm set for {DateTime.Today.Add(tod):h:mm tt}{(daily ? ", every day" : "")}.", $"Alarm · {DateTime.Today.Add(tod):h:mm tt}");
                }
                case "system_status":
                {
                    var (percent, used, total) = VitalsService.Ram();
                    var top = await Task.Run(() => VitalsService.TopApps(4));
                    var apps = string.Join(", ", top.Select(a => $"{a.Name} {a.GigaBytes:0.0} GB"));
                    return (true, $"RAM {percent:0}% used ({used:0.0} of {total:0.0} GB). Biggest memory users: {apps}.", $"RAM {percent:0}%");
                }
                case "free_ram":
                {
                    var mode = Str("level").ToLowerInvariant() switch { "deep" or "more" => RamCleanMode.More, "max" or "maximum" => RamCleanMode.Maximum, _ => RamCleanMode.Normal };
                    var result = await VitalsService.CleanAsync(mode);
                    _pet.React(PetEvent.RamFreed, result.FreedGb >= 0.3 ? $"{result.FreedGb:0.0} GB" : null);
                    return (true, $"Freed about {result.FreedGb:0.0} GB. {result.Note}.", $"Freed {result.FreedGb:0.0} GB");
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

    private static int ParseUrgency(string text) => text.Trim().ToLowerInvariant() switch { "urgent" or "critical" or "asap" or "2" => 2, "high" or "important" or "soon" or "1" => 1, _ => 0 };

    private static readonly string[] SettableNames =
    {
        "Mode", "Material", "IslandScale", "CornerRoundness", "GlassBlur", "GlassShine", "GlassRim", "GlassVibrancy", "GlassRefraction", "GlassTint",
        "PetEnabled", "PetKind", "PetOutfit", "PetTalks", "PetReminders", "PetReminderMinutes", "WaveStyle", "EqualizerLook", "ProgressStyle", "ClockStyle", "ClockFont", "ClockLook",
        "ShowSeconds", "ShowDateWhenIdle", "BarRevealSeconds", "AlertSeconds", "SnoozeMinutes", "FocusMinutes", "ShortBreakMinutes", "LongBreakMinutes",
        "VitalsAlertEnabled", "VitalsAlertPercent", "LyricsEnabled", "NotificationsEnabled", "FollowCursorMonitor",
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
