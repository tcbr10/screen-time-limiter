using ScreenLimiter.Core;

namespace ScreenLimiter.Core.Tests;

public class RulesEngineTests
{
    // 2026-11-15 is a Sunday.
    private static readonly DateTime Sunday5pm = new(2026, 11, 15, 17, 0, 0);

    private static Verdict Eval(UserPolicy p, UserUsage u, DateTime local) =>
        RulesEngine.Evaluate(p, u, local, local.ToUniversalTime());

    [Fact]
    public void No_limit_mode_is_always_allowed()
    {
        var v = Eval(new UserPolicy { Mode = LimitMode.None }, new UserUsage { UsedTodaySeconds = 999_999 }, Sunday5pm);
        Assert.False(v.Limited);
        Assert.True(v.Allowed);
    }

    [Fact]
    public void Daily_same_for_all_days_counts_down()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, SameForAllDays = true, DailyMinutesAll = 120 };
        var v = Eval(p, new UserUsage { UsedTodaySeconds = 48 * 60 }, Sunday5pm);
        Assert.True(v.Allowed);
        Assert.Equal(72 * 60, v.RemainingSeconds);
        Assert.Equal(120 * 60, v.LimitSeconds);
    }

    [Fact]
    public void Daily_blocks_when_used_up()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 60 };
        var v = Eval(p, new UserUsage { UsedTodaySeconds = 3600 }, Sunday5pm);
        Assert.False(v.Allowed);
        Assert.Equal(0, v.RemainingSeconds);
    }

    [Fact]
    public void Daily_per_day_uses_the_weekday_value()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, SameForAllDays = false };
        p.DailyMinutes[(int)DayOfWeek.Sunday] = 30;
        p.DailyMinutes[(int)DayOfWeek.Monday] = 90;

        Assert.Equal(30 * 60, Eval(p, new UserUsage(), Sunday5pm).RemainingSeconds);
        Assert.Equal(90 * 60, Eval(p, new UserUsage(), Sunday5pm.AddDays(1)).RemainingSeconds);
    }

    [Fact]
    public void Daily_zero_minutes_means_blocked_all_day()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 0 };
        Assert.False(Eval(p, new UserUsage(), Sunday5pm).Allowed);
    }

    [Fact]
    public void Weekly_counts_against_the_week()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 600 };
        var v = Eval(p, new UserUsage { UsedWeekSeconds = 500 * 60, UsedTodaySeconds = 10 * 60 }, Sunday5pm);
        Assert.Equal(100 * 60, v.RemainingSeconds);
    }

    [Fact]
    public void Weekly_daily_cap_wins_when_it_is_tighter()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 600, WeeklyDailyCapMinutes = 120 };
        var v = Eval(p, new UserUsage { UsedWeekSeconds = 100 * 60, UsedTodaySeconds = 100 * 60 }, Sunday5pm);
        Assert.Equal(20 * 60, v.RemainingSeconds);
        Assert.Equal(120 * 60, v.LimitSeconds);
    }

    [Fact]
    public void Weekly_total_wins_when_it_is_tighter_than_the_cap()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 600, WeeklyDailyCapMinutes = 120 };
        var v = Eval(p, new UserUsage { UsedWeekSeconds = 590 * 60, UsedTodaySeconds = 0 }, Sunday5pm);
        Assert.Equal(10 * 60, v.RemainingSeconds);
    }

    [Theory]
    [InlineData(15, 59, false, 0)]
    [InlineData(16, 0, true, 4 * 3600)]
    [InlineData(19, 30, true, 30 * 60)]
    [InlineData(20, 0, false, 0)]
    public void Allowed_hours_follow_the_window(int hour, int minute, bool allowed, int remaining)
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 960, EndMinute = 1200 } };
        var v = Eval(p, new UserUsage(), new DateTime(2026, 11, 15, hour, minute, 0));
        Assert.Equal(allowed, v.Allowed);
        Assert.Equal(remaining, v.RemainingSeconds);
    }

    [Fact]
    public void Allowed_hours_per_day_uses_the_weekday_window()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, SameForAllDays = false };
        p.Windows[(int)DayOfWeek.Sunday] = new TimeWindow { StartMinute = 600, EndMinute = 720 };
        p.Windows[(int)DayOfWeek.Monday] = new TimeWindow { StartMinute = 960, EndMinute = 1200 };

        Assert.False(Eval(p, new UserUsage(), Sunday5pm).Allowed);
        Assert.True(Eval(p, new UserUsage(), Sunday5pm.AddDays(1)).Allowed);
    }

    [Fact]
    public void Allowed_hours_window_can_end_at_midnight()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 1200, EndMinute = 1440 } };
        var v = Eval(p, new UserUsage(), new DateTime(2026, 11, 15, 23, 59, 0));
        Assert.True(v.Allowed);
        Assert.Equal(60, v.RemainingSeconds);
    }

    [Fact]
    public void Grant_extends_a_daily_limit()
    {
        var p = new UserPolicy { Mode = LimitMode.Daily, DailyMinutesAll = 60 };
        var u = new UserUsage { UsedTodaySeconds = 3600 };
        RulesEngine.Grant(p, u, 30, Sunday5pm, Sunday5pm.ToUniversalTime());

        var v = Eval(p, u, Sunday5pm);
        Assert.True(v.Allowed);
        Assert.Equal(30 * 60, v.RemainingSeconds);
    }

    [Fact]
    public void Grant_extends_both_weekly_total_and_daily_cap()
    {
        var p = new UserPolicy { Mode = LimitMode.Weekly, WeeklyMinutes = 100, WeeklyDailyCapMinutes = 50 };
        var u = new UserUsage { UsedWeekSeconds = 100 * 60, UsedTodaySeconds = 50 * 60 };
        RulesEngine.Grant(p, u, 15, Sunday5pm, Sunday5pm.ToUniversalTime());
        Assert.Equal(15 * 60, Eval(p, u, Sunday5pm).RemainingSeconds);
    }

    [Fact]
    public void Grant_outside_allowed_hours_opens_a_timed_override()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 960, EndMinute = 1200 } };
        var u = new UserUsage();
        var at9pm = new DateTime(2026, 11, 15, 21, 0, 0);
        RulesEngine.Grant(p, u, 30, at9pm, at9pm.ToUniversalTime());

        Assert.Equal(30 * 60, Eval(p, u, at9pm).RemainingSeconds);
        Assert.True(Eval(p, u, at9pm.AddMinutes(29)).Allowed);
        Assert.False(Eval(p, u, at9pm.AddMinutes(31)).Allowed);
    }

    [Fact]
    public void Grant_inside_allowed_hours_adds_after_the_window_ends()
    {
        var p = new UserPolicy { Mode = LimitMode.AllowedHours, WindowAll = new TimeWindow { StartMinute = 960, EndMinute = 1200 } };
        var u = new UserUsage();
        var at730pm = new DateTime(2026, 11, 15, 19, 30, 0);
        RulesEngine.Grant(p, u, 30, at730pm, at730pm.ToUniversalTime());

        Assert.Equal(60 * 60, Eval(p, u, at730pm).RemainingSeconds);
        Assert.True(Eval(p, u, new DateTime(2026, 11, 15, 20, 15, 0)).Allowed);
        Assert.False(Eval(p, u, new DateTime(2026, 11, 15, 20, 31, 0)).Allowed);
    }

    [Fact]
    public void Rollover_resets_the_day_but_keeps_the_week()
    {
        var sunday = new DateOnly(2026, 11, 15);
        var u = new UserUsage();
        RulesEngine.Rollover(u, sunday, DayOfWeek.Sunday);
        RulesEngine.AddUsage(u, 600);
        RulesEngine.Grant(new UserPolicy { Mode = LimitMode.Daily }, u, 10, Sunday5pm, Sunday5pm.ToUniversalTime());

        RulesEngine.Rollover(u, sunday.AddDays(1), DayOfWeek.Sunday);

        Assert.Equal(0, u.UsedTodaySeconds);
        Assert.Equal(0, u.BonusTodaySeconds);
        Assert.Equal(600, u.UsedWeekSeconds);
        Assert.Equal(600, u.BonusWeekSeconds);
    }

    [Fact]
    public void Rollover_resets_the_week_on_its_first_day()
    {
        var saturday = new DateOnly(2026, 11, 14);
        var u = new UserUsage();
        RulesEngine.Rollover(u, saturday, DayOfWeek.Sunday);
        RulesEngine.AddUsage(u, 600);

        RulesEngine.Rollover(u, saturday.AddDays(1), DayOfWeek.Sunday);

        Assert.Equal(0, u.UsedWeekSeconds);
        Assert.Equal(new DateOnly(2026, 11, 15), u.WeekStart);
    }

    [Fact]
    public void Rollover_ignores_a_clock_moved_backwards()
    {
        var sunday = new DateOnly(2026, 11, 15);
        var u = new UserUsage();
        RulesEngine.Rollover(u, sunday, DayOfWeek.Sunday);
        RulesEngine.AddUsage(u, 600);

        RulesEngine.Rollover(u, sunday.AddDays(-3), DayOfWeek.Sunday);

        Assert.Equal(sunday, u.Day);
        Assert.Equal(600, u.UsedTodaySeconds);
    }

    [Theory]
    [InlineData(2026, 11, 15, DayOfWeek.Sunday, 15)]
    [InlineData(2026, 11, 18, DayOfWeek.Sunday, 15)]
    [InlineData(2026, 11, 21, DayOfWeek.Sunday, 15)]
    [InlineData(2026, 11, 15, DayOfWeek.Monday, 9)]
    public void Week_start_is_computed_from_the_first_day(int y, int m, int d, DayOfWeek first, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 11, expectedDay), RulesEngine.WeekStartOf(new DateOnly(y, m, d), first));
    }
}

public class PinHasherTests
{
    [Fact]
    public void Verifies_the_right_pin_only()
    {
        var hash = PinHasher.Hash("4821");
        Assert.True(PinHasher.Verify("4821", hash));
        Assert.False(PinHasher.Verify("4822", hash));
        Assert.DoesNotContain("4821", hash);
    }

    [Fact]
    public void Rejects_missing_or_garbled_hashes()
    {
        Assert.False(PinHasher.Verify("1234", null));
        Assert.False(PinHasher.Verify("1234", "not-a-hash"));
        Assert.False(PinHasher.Verify("1234", "pbkdf2$x$y$z"));
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    public void Pin_format_is_four_to_eight_digits(string pin, bool valid)
    {
        Assert.Equal(valid, PinHasher.IsValidFormat(pin));
    }
}

public class ModelTests
{
    [Fact]
    public void Config_survives_a_json_round_trip()
    {
        var config = new AppConfig { Language = "en" };
        config.Users.Add(new UserPolicy { Sid = "S-1-5-21-1", UserName = "Noa", Mode = LimitMode.AllowedHours, SameForAllDays = false });

        var copy = Json.Clone(config);

        Assert.Equal("en", copy.Language);
        Assert.Equal(LimitMode.AllowedHours, copy.Users[0].Mode);
        Assert.Equal(7, copy.Users[0].Windows.Length);
    }

    [Fact]
    public void Normalize_repairs_out_of_range_values()
    {
        var p = new UserPolicy { DailyMinutes = [1, 2], DailyMinutesAll = 99999, WindowAll = new TimeWindow { StartMinute = 900, EndMinute = 100 } };
        p.Normalize();

        Assert.Equal(7, p.DailyMinutes.Length);
        Assert.Equal(1440, p.DailyMinutesAll);
        Assert.Equal(900, p.WindowAll.EndMinute);
    }
}
