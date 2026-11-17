using ScreenLimiter.Core;

namespace ScreenLimiter.Core.Tests;

public class NextAllowedTests
{
    // 2026-11-15 is a Sunday.
    private static readonly DateTime Sunday5pm = new(2026, 11, 15, 17, 0, 0);
    private static readonly DateTime Utc = new(2026, 11, 15, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Usage_stops_counting_at_the_limit()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 3 };
        var u = new UserUsage { UsedTodaySeconds = 170 };
        var v = RulesEngine.CountUsage(p, u, 30, Sunday5pm, Utc);
        Assert.False(v.Allowed);
        Assert.Equal(180, u.UsedTodaySeconds);
        Assert.Equal(10, u.UsedWeekSeconds);

        // Sitting in front of the lock screen adds nothing.
        RulesEngine.CountUsage(p, u, 300, Sunday5pm, Utc);
        Assert.Equal(180, u.UsedTodaySeconds);
    }

    [Fact]
    public void Granted_time_is_available_in_full_after_waiting_at_the_lock_screen()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 3 };
        var u = new UserUsage { UsedTodaySeconds = 180 };
        RulesEngine.CountUsage(p, u, 600, Sunday5pm, Utc);
        RulesEngine.Grant(p, u, 15, Sunday5pm, Utc);
        Assert.Equal(15 * 60, RulesEngine.Evaluate(p, u, Sunday5pm, Utc).RemainingSeconds);
    }

    [Fact]
    public void Usage_is_not_counted_outside_allowed_hours()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 18 * 60, EndMinute = 20 * 60 } };
        var u = new UserUsage();
        RulesEngine.CountUsage(p, u, 10, Sunday5pm, Utc);
        Assert.Equal(0, u.UsedTodaySeconds);
    }

    [Fact]
    public void Daily_comes_back_tomorrow_at_midnight()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 60 };
        var next = RulesEngine.NextAllowed(p, new UserUsage { UsedTodaySeconds = 3600 }, Sunday5pm, DayOfWeek.Sunday);
        Assert.Equal(new DateTime(2026, 11, 16), next);
    }

    [Fact]
    public void Daily_skips_days_set_to_zero()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, SameForAllDays = false, DailyMinutes = [60, 0, 0, 60, 60, 60, 60] };
        var next = RulesEngine.NextAllowed(p, new UserUsage(), Sunday5pm, DayOfWeek.Sunday);
        Assert.Equal(new DateTime(2026, 11, 18), next); // Wednesday
    }

    [Fact]
    public void Daily_with_every_day_zero_has_no_next_time()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 0 };
        Assert.Null(RulesEngine.NextAllowed(p, new UserUsage(), Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Weekly_used_up_comes_back_next_week()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 60 };
        var u = new UserUsage { UsedWeekSeconds = 3600, UsedTodaySeconds = 3600 };
        Assert.Equal(BlockReason.Week, RulesEngine.ReasonBlocked(p, u));
        Assert.Equal(new DateTime(2026, 11, 22), RulesEngine.NextAllowed(p, u, Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Weekly_daily_cap_comes_back_tomorrow()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 600, WeeklyDailyCapMinutes = 60 };
        var u = new UserUsage { UsedWeekSeconds = 3600, UsedTodaySeconds = 3600 };
        Assert.Equal(BlockReason.Day, RulesEngine.ReasonBlocked(p, u));
        Assert.Equal(new DateTime(2026, 11, 16), RulesEngine.NextAllowed(p, u, Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Allowed_hours_before_the_window_opens_today()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 18 * 60, EndMinute = 20 * 60 } };
        Assert.Equal(new DateTime(2026, 11, 15, 18, 0, 0), RulesEngine.NextAllowed(p, new UserUsage(), Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Allowed_hours_after_the_window_opens_tomorrow()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 16 * 60 + 30, EndMinute = 16 * 60 + 45 } };
        Assert.Equal(new DateTime(2026, 11, 16, 16, 30, 0), RulesEngine.NextAllowed(p, new UserUsage(), Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Allowed_hours_skip_empty_days()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, SameForAllDays = false };
        for (var i = 0; i < 7; i++) p.Windows[i] = new TimeWindow { StartMinute = 600, EndMinute = 600 };
        p.Windows[5] = new TimeWindow { StartMinute = 9 * 60, EndMinute = 12 * 60 }; // Friday
        Assert.Equal(new DateTime(2026, 11, 20, 9, 0, 0), RulesEngine.NextAllowed(p, new UserUsage(), Sunday5pm, DayOfWeek.Sunday));
    }

    [Fact]
    public void Local_time_follows_the_saved_time_zone_not_the_pc()
    {
        var utc = new DateTime(2026, 11, 15, 21, 30, 0, DateTimeKind.Utc);
        var config = new AppConfig { TimeZoneId = OperatingSystem.IsWindows() ? "Israel Standard Time" : "Asia/Jerusalem" };
        Assert.Equal(new DateTime(2026, 11, 15, 23, 30, 0), config.LocalNow(utc));

        config.TimeZoneId = "No/Such_Zone";
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local), config.LocalNow(utc));
    }
}
