// Lumen (personal fork) - smart time setup: suggestions for alarms and timers that adapt to the time of day,
// to what is already set, and to real habits (not one-off sets). Everything it learns stays in settings.json.

using System.Globalization;

namespace DIsland.Services;

/// <summary>One tappable suggestion: what to show, why, and the value it sets.</summary>
public sealed record TimeSuggestion(string Label, string Reason, TimeSpan TimeOfDay = default, int Minutes = 0);

/// <summary>
/// A learned wake-time habit. Built from sets / rings / deletes across days — spam-clicking
/// "Set alarm" four times in a minute does not make a usual.
/// </summary>
public sealed class AlarmHabit
{
    /// <summary>5-minute bucket, "HH:mm".</summary>
    public string Time { get; set; } = "07:00";
    public int Sets { get; set; }
    /// <summary>Calendar days that contributed a set (the real habit signal).</summary>
    public int DistinctDays { get; set; }
    /// <summary>Last day a set counted, "yyyy-MM-dd" — same-day repeats are ignored.</summary>
    public string? LastDay { get; set; }
    public int Rings { get; set; }
    public int Deletes { get; set; }
    /// <summary>Sun..Sat hit counts for day-of-week affinity.</summary>
    public int[] Dow { get; set; } = new int[7];
    public DateTime FirstAt { get; set; }
    public DateTime LastAt { get; set; }
}

public static class SmartTime
{
    private const int MinutesToFallAsleep = 14;
    private const int SleepCycleMinutes = 90;
    private const int BucketMinutes = 5;
    private const double HalfLifeDays = 18;
    /// <summary>Below this, never label something "your usual".</summary>
    private const double UsualScoreGate = 0.48;

    private static string Key(TimeSpan t) => Bucket(t).ToString(@"hh\:mm");

    /// <summary>Snap to a 5-minute grid so 7:00 / 7:03 / 7:05 are one habit.</summary>
    public static TimeSpan Bucket(TimeSpan t)
    {
        var mins = (int)Math.Round(t.TotalMinutes / BucketMinutes) * BucketMinutes;
        mins = ((mins % 1440) + 1440) % 1440;
        return TimeSpan.FromMinutes(mins);
    }

    public static void RecordAlarm(AppSettings s, TimeSpan timeOfDay)
    {
        var now = DateTime.Now;
        var habit = GetOrCreate(s, timeOfDay);
        var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Same calendar day: ignore. Four frantic clicks ≠ four days of habit.
        if (habit.LastDay == day) return;

        habit.Sets++;
        habit.DistinctDays++;
        habit.LastDay = day;
        habit.Dow[ClampDow(now.DayOfWeek)]++;
        if (habit.FirstAt == default) habit.FirstAt = now;
        habit.LastAt = now;
        TrimHabits(s);
        // Keep the old counter warm for anything still reading it.
        var key = Key(timeOfDay);
        s.AlarmUse[key] = s.AlarmUse.TryGetValue(key, out var n) ? n + 1 : 1;
        Trim(s.AlarmUse);
    }

    public static void RecordAlarmRemoved(AppSettings s, TimeSpan timeOfDay)
    {
        var habit = Find(s, timeOfDay);
        if (habit is null) return;
        habit.Deletes++;
        habit.LastAt = DateTime.Now;
    }

    public static void RecordAlarmRang(AppSettings s, TimeSpan timeOfDay)
    {
        var habit = GetOrCreate(s, timeOfDay);
        habit.Rings++;
        habit.LastAt = DateTime.Now;
        if (habit.FirstAt == default) habit.FirstAt = DateTime.Now;
        TrimHabits(s);
    }

    public static void RecordTimer(AppSettings s, int minutes)
    {
        var key = minutes.ToString(CultureInfo.InvariantCulture);
        s.TimerUse[key] = s.TimerUse.TryGetValue(key, out var n) ? n + 1 : 1;
        Trim(s.TimerUse);
    }

    /// <summary>One-time: turn old flat AlarmUse counts into weak habit seeds (spam stays below the gate).</summary>
    public static void MigrateAlarmUse(AppSettings s)
    {
        if (s.AlarmHabits.Count > 0 || s.AlarmUse.Count == 0) return;
        foreach (var (key, count) in s.AlarmUse)
        {
            if (count < 1 || !TimeSpan.TryParse(key, out var t)) continue;
            var habit = GetOrCreate(s, t);
            habit.Sets = Math.Max(habit.Sets, count);
            // Unknown days: a handful of clicks → 1 day; a long history → a few days, never a free pass.
            habit.DistinctDays = Math.Max(habit.DistinctDays, count >= 8 ? 4 : count >= 5 ? 2 : 1);
            habit.LastAt = DateTime.Now.AddDays(-Math.Min(30, count));
            if (habit.FirstAt == default) habit.FirstAt = habit.LastAt.AddDays(-habit.DistinctDays);
        }
    }

    private static AlarmHabit GetOrCreate(AppSettings s, TimeSpan timeOfDay)
    {
        var key = Key(timeOfDay);
        var hit = s.AlarmHabits.FirstOrDefault(h => h.Time == key);
        if (hit is not null) return hit;
        hit = new AlarmHabit { Time = key };
        s.AlarmHabits.Add(hit);
        return hit;
    }

    private static AlarmHabit? Find(AppSettings s, TimeSpan timeOfDay) =>
        s.AlarmHabits.FirstOrDefault(h => h.Time == Key(timeOfDay));

    private static int ClampDow(DayOfWeek d) => (int)d; // Sunday = 0 … Saturday = 6

    private static void Trim(Dictionary<string, int> map)
    {
        while (map.Count > 24) map.Remove(map.OrderBy(p => p.Value).First().Key);
    }

    private static void TrimHabits(AppSettings s)
    {
        while (s.AlarmHabits.Count > 16)
        {
            var worst = s.AlarmHabits.OrderBy(h => Score(h)).First();
            s.AlarmHabits.Remove(worst);
        }
    }

    private static DateTime RoundUp(DateTime t, int minutes)
    {
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime((t.Ticks + ticks - 1) / ticks * ticks);
    }

    /// <summary>
    /// Habit strength 0..~1.4. Needs distinct days + recency + weekday fit; rings help, quick deletes hurt.
    /// </summary>
    public static double Score(AlarmHabit h, DateTime? now = null)
    {
        now ??= DateTime.Now;
        if (h.Sets < 1 || h.DistinctDays < 1) return 0;

        var ageDays = Math.Max(0, (now.Value - h.LastAt).TotalDays);
        var recency = Math.Exp(-ageDays / HalfLifeDays);                    // decays over ~weeks
        var spanDays = Math.Max(1, (h.LastAt - (h.FirstAt == default ? h.LastAt : h.FirstAt)).TotalDays);
        var consistency = h.DistinctDays / Math.Max(spanDays / 7.0 * 3.0, h.DistinctDays); // denser weeks score higher
        consistency = Math.Clamp(consistency, 0, 1);
        var daySpread = Math.Clamp(h.DistinctDays / 5.0, 0, 1);             // ~5 different days → full
        var freq = Math.Log(1 + h.Sets) / Math.Log(1 + 14);                 // diminishing returns
        freq = Math.Clamp(freq, 0, 1);

        var dowTotal = h.Dow.Sum();
        var dowHit = dowTotal > 0 ? h.Dow[ClampDow(now.Value.DayOfWeek)] / (double)dowTotal : 0;
        // Mild prior so a brand-new habit isn't zeroed on an unmatched weekday
        var dowAffinity = dowTotal >= 3 ? 0.25 + 0.75 * dowHit : 0.45;

        var ringBonus = Math.Min(0.28, h.Rings * 0.07);
        var deleteRate = h.Deletes / (double)Math.Max(1, h.Sets);
        var deletePenalty = Math.Clamp(deleteRate * 0.55, 0, 0.55);

        var core = 0.28 * freq + 0.32 * daySpread + 0.18 * consistency + 0.12 * recency + 0.10 * dowAffinity;
        return Math.Max(0, core * (1.0 - deletePenalty) + ringBonus);
    }

    private static bool IsUsual(AlarmHabit h, DateTime now)
    {
        var score = Score(h, now);
        if (score < UsualScoreGate) return false;
        // Hard floor: at least 3 distinct days, or 2 days + a couple of real rings.
        if (h.DistinctDays >= 3 && h.Sets >= 3) return true;
        if (h.DistinctDays >= 2 && h.Rings >= 2 && score >= 0.55) return true;
        return false;
    }

    private static string UsualReason(AlarmHabit h, DateTime now)
    {
        var dowTotal = h.Dow.Sum();
        if (dowTotal >= 4)
        {
            var today = ClampDow(now.DayOfWeek);
            var peak = 0;
            for (var i = 1; i < 7; i++) if (h.Dow[i] > h.Dow[peak]) peak = i;
            // Strong weekday lean (e.g. weekday mornings)
            if (h.Dow[today] >= 2 && h.Dow[today] >= h.Dow[peak] * 0.7)
            {
                var name = now.DayOfWeek switch
                {
                    DayOfWeek.Monday => "Mondays",
                    DayOfWeek.Tuesday => "Tuesdays",
                    DayOfWeek.Wednesday => "Wednesdays",
                    DayOfWeek.Thursday => "Thursdays",
                    DayOfWeek.Friday => "Fridays",
                    DayOfWeek.Saturday => "Saturdays",
                    _ => "Sundays",
                };
                return $"usual {name}";
            }
        }
        return Score(h, now) >= 0.72 ? "your usual" : "often";
    }

    /// <summary>The alarm time worth starting the picker on: strongest usual, else the next half hour.</summary>
    public static TimeSpan DefaultAlarm(AppSettings s, DateTime now)
    {
        MigrateAlarmUse(s);
        var best = s.AlarmHabits
            .Where(h => IsUsual(h, now))
            .OrderByDescending(h => Score(h, now))
            .Select(h => TimeSpan.TryParse(h.Time, out var t) ? t : (TimeSpan?)null)
            .FirstOrDefault(t => t.HasValue);
        return best ?? RoundUp(now, 30).TimeOfDay;
    }

    /// <summary>Up to four alarm suggestions: real habits first, then sleep-cycle / soon / tomorrow fillers.</summary>
    public static List<TimeSuggestion> AlarmSuggestions(AppSettings s, DateTime now)
    {
        MigrateAlarmUse(s);
        var list = new List<TimeSuggestion>();
        var already = s.Alarms.Where(a => a.Enabled).Select(a => Key(a.TimeOfDay)).ToHashSet();

        foreach (var habit in s.AlarmHabits.OrderByDescending(h => Score(h, now)))
        {
            if (!IsUsual(habit, now) || already.Contains(habit.Time) || !TimeSpan.TryParse(habit.Time, out var t))
                continue;
            list.Add(new TimeSuggestion(Clock(t), UsualReason(habit, now), t));
            if (list.Count == 2) break;
        }

        var late = now.Hour >= 21 || now.Hour < 4;
        if (late)
        {
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
            foreach (var (minutes, reason) in new[] { (30, "in 30 min"), (60, "in 1 hour"), (120, "in 2 hours") })
            {
                var t = RoundUp(now.AddMinutes(minutes), 5).TimeOfDay;
                list.Add(new TimeSuggestion(Clock(t), reason, t));
            }
            if (now.Hour >= 16)
                list.Insert(Math.Min(list.Count, 2), new TimeSuggestion(Clock(new TimeSpan(7, 0, 0)), "tomorrow", new TimeSpan(7, 0, 0)));
        }

        return list.GroupBy(x => Key(x.TimeOfDay)).Select(g => g.First()).Where(x => !already.Contains(Key(x.TimeOfDay))).Take(4).ToList();
    }

    /// <summary>Timer lengths to offer: the basics, your most used ones, and one that fits the moment.</summary>
    public static List<TimeSuggestion> TimerSuggestions(AppSettings s, DateTime now, int focusSessionsToday)
    {
        var minutes = new List<int> { 5, 10, 25 };
        foreach (var (key, count) in s.TimerUse.OrderByDescending(p => p.Value))
            if (count >= 2 && int.TryParse(key, out var m) && !minutes.Contains(m) && m is > 0 and <= 240) minutes.Add(m);

        if (focusSessionsToday > 0 && !minutes.Contains(15)) minutes.Add(15);
        if (now.Hour is >= 11 and <= 14 or >= 18 and <= 21) { if (!minutes.Contains(12)) minutes.Add(12); }
        if (now.Hour >= 22 || now.Hour < 5) { if (!minutes.Contains(45)) minutes.Add(45); }
        if (s.FocusEnabled && !minutes.Contains(s.FocusMinutes)) minutes.Add(s.FocusMinutes);

        var top = s.TimerUse.OrderByDescending(p => p.Value).FirstOrDefault();
        return minutes.Distinct().OrderBy(m => m).Take(7)
            .Select(m => new TimeSuggestion(m >= 60 && m % 60 == 0 ? $"{m / 60}h" : $"{m}m", top.Key == m.ToString(CultureInfo.InvariantCulture) && top.Value >= 2 ? "usual" : "", Minutes: m))
            .ToList();
    }

    public static string Clock(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt", CultureInfo.CurrentCulture);
}
