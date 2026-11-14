// Lumen (personal fork) - smart time setup: suggestions for alarms and timers that adapt to the time of day, to what
// is already set, and to what you actually use. Everything it learns is a pair of small counters kept in the settings file.

using System.Globalization;

namespace DIsland.Services;

/// <summary>One tappable suggestion: what to show, why, and the value it sets.</summary>
public sealed record TimeSuggestion(string Label, string Reason, TimeSpan TimeOfDay = default, int Minutes = 0);

public static class SmartTime
{
    private const int MinutesToFallAsleep = 14;
    private const int SleepCycleMinutes = 90;

    private static string Key(TimeSpan t) => t.ToString(@"hh\:mm");

    public static void RecordAlarm(AppSettings s, TimeSpan timeOfDay)
    {
        var key = Key(timeOfDay);
        s.AlarmUse[key] = s.AlarmUse.TryGetValue(key, out var n) ? n + 1 : 1;
        Trim(s.AlarmUse);
    }

    public static void RecordTimer(AppSettings s, int minutes)
    {
        var key = minutes.ToString(CultureInfo.InvariantCulture);
        s.TimerUse[key] = s.TimerUse.TryGetValue(key, out var n) ? n + 1 : 1;
        Trim(s.TimerUse);
    }

    private static void Trim(Dictionary<string, int> map)
    {
        while (map.Count > 24) map.Remove(map.OrderBy(p => p.Value).First().Key);
    }

    private static DateTime RoundUp(DateTime t, int minutes)
    {
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime((t.Ticks + ticks - 1) / ticks * ticks);
    }

    /// <summary>The alarm time worth starting the picker on: your usual one, or the next half hour.</summary>
    public static TimeSpan DefaultAlarm(AppSettings s, DateTime now)
    {
        var usual = s.AlarmUse.OrderByDescending(p => p.Value).Select(p => TimeSpan.TryParse(p.Key, out var t) ? t : (TimeSpan?)null).FirstOrDefault(t => t.HasValue);
        return usual ?? RoundUp(now, 30).TimeOfDay;
    }

    /// <summary>Up to four alarm suggestions: sleep-cycle wake-ups at night, "in 30 min" style during the day, plus your usual times.</summary>
    public static List<TimeSuggestion> AlarmSuggestions(AppSettings s, DateTime now)
    {
        var list = new List<TimeSuggestion>();
        var already = s.Alarms.Where(a => a.Enabled).Select(a => Key(a.TimeOfDay)).ToHashSet();

        // 1. what you usually set (and haven't already)
        foreach (var (key, count) in s.AlarmUse.OrderByDescending(p => p.Value).Take(6))
        {
            if (count < 2 || already.Contains(key) || !TimeSpan.TryParse(key, out var t)) continue;
            list.Add(new TimeSuggestion(Clock(t), "your usual", t));
            if (list.Count == 2) break;
        }

        var late = now.Hour >= 21 || now.Hour < 4;
        if (late)
        {
            // 2. sleep cycles: wake at the end of a 90 minute cycle (after about 14 minutes to fall asleep)
            var asleep = now.AddMinutes(MinutesToFallAsleep);
            foreach (var cycles in new[] { 5, 6 })
            {
                var wake = asleep.AddMinutes(cycles * SleepCycleMinutes);
                var t = RoundUp(wake, 5).TimeOfDay;
                list.Add(new TimeSuggestion(Clock(t), $"{cycles * 1.5:0.#} h of sleep", t));
            }
        }
        else
        {
            // 3. soon: a short nap or reminder
            foreach (var (minutes, reason) in new[] { (30, "in 30 min"), (60, "in 1 hour"), (120, "in 2 hours") })
            {
                var t = RoundUp(now.AddMinutes(minutes), 5).TimeOfDay;
                list.Add(new TimeSuggestion(Clock(t), reason, t));
            }
            if (now.Hour >= 16)
            {
                // evening: tomorrow morning is the next likely alarm
                list.Insert(Math.Min(list.Count, 2), new TimeSuggestion(Clock(new TimeSpan(7, 0, 0)), "tomorrow", new TimeSpan(7, 0, 0)));
            }
        }

        return list.GroupBy(x => x.TimeOfDay).Select(g => g.First()).Where(x => !already.Contains(Key(x.TimeOfDay))).Take(4).ToList();
    }

    /// <summary>Timer lengths to offer: the basics, your most used ones, and one that fits the moment.</summary>
    public static List<TimeSuggestion> TimerSuggestions(AppSettings s, DateTime now, int focusSessionsToday)
    {
        var minutes = new List<int> { 5, 10, 25 };
        foreach (var (key, count) in s.TimerUse.OrderByDescending(p => p.Value))
            if (count >= 2 && int.TryParse(key, out var m) && !minutes.Contains(m) && m is > 0 and <= 240) minutes.Add(m);

        // fits the moment
        if (focusSessionsToday > 0 && !minutes.Contains(15)) minutes.Add(15);          // a proper break after working
        if (now.Hour is >= 11 and <= 14 or >= 18 and <= 21) { if (!minutes.Contains(12)) minutes.Add(12); }  // cooking
        if (now.Hour >= 22 || now.Hour < 5) { if (!minutes.Contains(45)) minutes.Add(45); }                  // wind down
        if (s.FocusEnabled && !minutes.Contains(s.FocusMinutes)) minutes.Add(s.FocusMinutes);

        var top = s.TimerUse.OrderByDescending(p => p.Value).FirstOrDefault();
        return minutes.Distinct().OrderBy(m => m).Take(7)
            .Select(m => new TimeSuggestion(m >= 60 && m % 60 == 0 ? $"{m / 60}h" : $"{m}m", top.Key == m.ToString(CultureInfo.InvariantCulture) && top.Value >= 2 ? "usual" : "", Minutes: m))
            .ToList();
    }

    public static string Clock(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt", CultureInfo.CurrentCulture);
}
