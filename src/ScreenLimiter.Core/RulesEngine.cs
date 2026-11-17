namespace ScreenLimiter.Core;

/// <summary>The answer to "may this user use the PC right now, and for how much longer?"</summary>
public readonly record struct Verdict(bool Limited, bool Allowed, int RemainingSeconds, int LimitSeconds, int UsedSeconds)
{
    public static readonly Verdict Unlimited = new(false, true, 0, 0, 0);
}

public enum BlockReason { Day, Week, Hours }

/// <summary>Pure rule evaluation. No clocks, files or Windows calls, so it can be tested anywhere.</summary>
public static class RulesEngine
{
    /// <summary>Evaluates a policy. Call <see cref="Rollover"/> first so the usage belongs to today.</summary>
    public static Verdict Evaluate(UserPolicy p, UserUsage u, DateTime localNow, DateTime utcNow)
    {
        var day = (int)localNow.DayOfWeek;
        double remaining, limit, used;

        switch (p.Mode)
        {
            case LimitMode.Daily:
                limit = (p.SameForAllDays ? p.DailyMinutesAll : p.DailyMinutes[day]) * 60.0 + u.BonusTodaySeconds;
                used = u.UsedTodaySeconds;
                remaining = limit - used;
                break;

            case LimitMode.Weekly:
                limit = p.WeeklyMinutes * 60.0 + u.BonusWeekSeconds;
                used = u.UsedWeekSeconds;
                remaining = limit - used;
                if (p.WeeklyDailyCapMinutes > 0)
                {
                    var capLimit = p.WeeklyDailyCapMinutes * 60.0 + u.BonusTodaySeconds;
                    var capRemaining = capLimit - u.UsedTodaySeconds;
                    if (capRemaining < remaining)
                    {
                        limit = capLimit;
                        used = u.UsedTodaySeconds;
                        remaining = capRemaining;
                    }
                }
                break;

            case LimitMode.AllowedHours:
                var w = p.SameForAllDays ? p.WindowAll : p.Windows[day];
                var now = localNow.TimeOfDay.TotalSeconds;
                double start = w.StartMinute * 60.0, end = w.EndMinute * 60.0;
                limit = Math.Max(0, end - start);
                if (now >= start && now < end)
                {
                    remaining = end - now;
                    used = now - start;
                }
                else
                {
                    remaining = 0;
                    used = limit;
                }
                if (u.OverrideUntilUtc is { } until && until > utcNow)
                {
                    var extra = (until - utcNow).TotalSeconds;
                    if (extra > remaining)
                    {
                        remaining = extra;
                        if (limit < remaining) limit = remaining;
                        used = limit - remaining;
                    }
                }
                break;

            default:
                return Verdict.Unlimited;
        }

        remaining = Math.Max(0, remaining);
        limit = Math.Max(0, limit);
        used = Math.Clamp(used, 0, limit);
        return new Verdict(true, remaining > 0, (int)Math.Ceiling(remaining), (int)limit, (int)used);
    }

    /// <summary>Adds parent-approved extra time on top of whatever is left right now.</summary>
    public static void Grant(UserPolicy p, UserUsage u, int minutes, DateTime localNow, DateTime utcNow)
    {
        if (minutes <= 0) return;
        var seconds = minutes * 60.0;
        if (p.Mode == LimitMode.AllowedHours)
        {
            var current = Evaluate(p, u, localNow, utcNow);
            u.OverrideUntilUtc = utcNow.AddSeconds(current.RemainingSeconds + seconds);
        }
        else
        {
            u.BonusTodaySeconds += seconds;
            u.BonusWeekSeconds += seconds;
        }
    }

    public static DateOnly WeekStartOf(DateOnly day, DayOfWeek firstDay)
    {
        var diff = ((int)day.DayOfWeek - (int)firstDay + 7) % 7;
        return day.AddDays(-diff);
    }

    /// <summary>
    /// Resets the daily and weekly counters when a new day or week begins.
    /// Does nothing if the date went backwards, so winding the clock back never hands out fresh time.
    /// </summary>
    public static void Rollover(UserUsage u, DateOnly today, DayOfWeek firstDay)
    {
        if (today <= u.Day) return;
        u.Day = today;
        u.UsedTodaySeconds = 0;
        u.BonusTodaySeconds = 0;

        var weekStart = WeekStartOf(today, firstDay);
        if (weekStart != u.WeekStart)
        {
            u.WeekStart = weekStart;
            u.UsedWeekSeconds = 0;
            u.BonusWeekSeconds = 0;
        }
    }

    /// <summary>Counts active screen time against both the daily and the weekly counter.</summary>
    public static void AddUsage(UserUsage u, double seconds)
    {
        if (seconds <= 0) return;
        u.UsedTodaySeconds += seconds;
        u.UsedWeekSeconds += seconds;
    }

    /// <summary>
    /// Counts screen time only while the user is allowed, and never past the limit. Time spent in front of
    /// the lock screen isn't use, so extra time a parent grants afterwards is available in full.
    /// </summary>
    public static Verdict CountUsage(UserPolicy p, UserUsage u, double seconds, DateTime localNow, DateTime utcNow)
    {
        var before = Evaluate(p, u, localNow, utcNow);
        if (before.Limited && before.Allowed) AddUsage(u, Math.Min(seconds, before.RemainingSeconds));
        return Evaluate(p, u, localNow, utcNow);
    }

    /// <summary>Why a user is blocked: used up today's time, this week's time, or outside the allowed hours.</summary>
    public static BlockReason ReasonBlocked(UserPolicy p, UserUsage u)
    {
        switch (p.Mode)
        {
            case LimitMode.Weekly:
                var weekLeft = p.WeeklyMinutes * 60.0 + u.BonusWeekSeconds - u.UsedWeekSeconds;
                return weekLeft > 0 ? BlockReason.Day : BlockReason.Week;
            case LimitMode.AllowedHours:
                return BlockReason.Hours;
            default:
                return BlockReason.Day;
        }
    }

    /// <summary>
    /// When the user may start again without a parent's help, in local time. Null if no time is coming in
    /// the next week (every day set to zero, for example).
    /// </summary>
    public static DateTime? NextAllowed(UserPolicy p, UserUsage u, DateTime localNow, DayOfWeek firstDay)
    {
        var today = localNow.Date;
        switch (p.Mode)
        {
            case LimitMode.Daily:
                for (var d = 1; d <= 7; d++)
                {
                    var day = today.AddDays(d);
                    var minutes = p.SameForAllDays ? p.DailyMinutesAll : p.DailyMinutes[(int)day.DayOfWeek];
                    if (minutes > 0) return day;
                }
                return null;

            case LimitMode.Weekly:
                if (ReasonBlocked(p, u) == BlockReason.Day) return today.AddDays(1);
                if (p.WeeklyMinutes <= 0) return null;
                var weekStart = WeekStartOf(DateOnly.FromDateTime(today), firstDay).AddDays(7);
                return weekStart.ToDateTime(TimeOnly.MinValue);

            case LimitMode.AllowedHours:
                for (var d = 0; d <= 7; d++)
                {
                    var day = today.AddDays(d);
                    var w = p.SameForAllDays ? p.WindowAll : p.Windows[(int)day.DayOfWeek];
                    if (w.EndMinute <= w.StartMinute) continue;
                    var start = day.AddMinutes(w.StartMinute);
                    if (start > localNow) return start;
                }
                return null;

            default:
                return null;
        }
    }
}
