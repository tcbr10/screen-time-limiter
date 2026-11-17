using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenLimiter.Core;

public enum LimitMode { None, Daily, Weekly, AllowedHours }

public enum TimeUpAction { Lock, SignOut }

/// <summary>A from–to window inside one day, in minutes after midnight. End may be 1440 (24:00).</summary>
public sealed class TimeWindow
{
    public int StartMinute { get; set; } = 16 * 60;
    public int EndMinute { get; set; } = 20 * 60;
}

/// <summary>The rules for one Windows account. Per-day arrays are indexed by (int)DayOfWeek, Sunday = 0.</summary>
public sealed class UserPolicy
{
    public string Sid { get; set; } = "";
    public string UserName { get; set; } = "";
    public LimitMode Mode { get; set; } = LimitMode.None;

    /// <summary>Applies to Daily and AllowedHours: one value for all days, or one per weekday.</summary>
    public bool SameForAllDays { get; set; } = true;

    public int DailyMinutesAll { get; set; } = 120;
    public int[] DailyMinutes { get; set; } = Enumerable.Repeat(120, 7).ToArray();

    public int WeeklyMinutes { get; set; } = 14 * 60;
    /// <summary>0 means no daily cap.</summary>
    public int WeeklyDailyCapMinutes { get; set; }

    public TimeWindow WindowAll { get; set; } = new();
    public TimeWindow[] Windows { get; set; } = NewWindows();

    public TimeUpAction OnTimeUp { get; set; } = TimeUpAction.Lock;
    public int[] WarnMinutes { get; set; } = [15, 5];

    private static TimeWindow[] NewWindows() => Enumerable.Range(0, 7).Select(_ => new TimeWindow()).ToArray();

    /// <summary>Repairs anything a hand-edited or older file could have left out of range.</summary>
    public void Normalize()
    {
        if (DailyMinutes is not { Length: 7 }) DailyMinutes = Enumerable.Repeat(DailyMinutesAll, 7).ToArray();
        if (Windows is not { Length: 7 } || Windows.Any(w => w is null)) Windows = NewWindows();
        WindowAll ??= new TimeWindow();
        WarnMinutes ??= [];

        DailyMinutesAll = Math.Clamp(DailyMinutesAll, 0, 1440);
        for (var i = 0; i < 7; i++) DailyMinutes[i] = Math.Clamp(DailyMinutes[i], 0, 1440);
        WeeklyMinutes = Math.Clamp(WeeklyMinutes, 0, 7 * 1440);
        WeeklyDailyCapMinutes = Math.Clamp(WeeklyDailyCapMinutes, 0, 1440);
        foreach (var w in Windows.Append(WindowAll))
        {
            w.StartMinute = Math.Clamp(w.StartMinute, 0, 1440);
            w.EndMinute = Math.Clamp(w.EndMinute, w.StartMinute, 1440);
        }
    }
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Stable identity of this PC. Unused locally; reserved for remote management.</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Language { get; set; } = "he";
    public string? PinHash { get; set; }
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Sunday;
    /// <summary>
    /// The Windows time zone the limits are counted in, taken when an administrator saves settings.
    /// Standard users may change the PC's time zone; this keeps that from moving "today" or the allowed hours.
    /// </summary>
    public string? TimeZoneId { get; set; }
    public List<UserPolicy> Users { get; set; } = [];

    /// <summary>The current local time in the saved time zone (the PC's own zone if none is saved yet).</summary>
    public DateTime LocalNow(DateTime utcNow)
    {
        var zone = TimeZoneInfo.Local;
        if (!string.IsNullOrEmpty(TimeZoneId))
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone), DateTimeKind.Unspecified);
    }

    public void Normalize()
    {
        Users ??= [];
        Users.RemoveAll(u => u is null || string.IsNullOrWhiteSpace(u.Sid));
        foreach (var u in Users) u.Normalize();
        if (Language != "he" && Language != "en") Language = "he";
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = Guid.NewGuid().ToString("N");
    }
}

public sealed class UserUsage
{
    public string Sid { get; set; } = "";
    public DateOnly Day { get; set; }
    public double UsedTodaySeconds { get; set; }
    public DateOnly WeekStart { get; set; }
    public double UsedWeekSeconds { get; set; }
    /// <summary>Extra time granted by a parent, for Daily and Weekly modes.</summary>
    public double BonusTodaySeconds { get; set; }
    public double BonusWeekSeconds { get; set; }
    /// <summary>Extra time granted by a parent in AllowedHours mode: allowed until this moment.</summary>
    public DateTime? OverrideUntilUtc { get; set; }
}

public sealed class UsageState
{
    public List<UserUsage> Users { get; set; } = [];
}

public static class Json
{
    public static readonly JsonSerializerOptions Compact = Make(false);
    public static readonly JsonSerializerOptions Indented = Make(true);

    private static JsonSerializerOptions Make(bool indented) => new()
    {
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Compact), Compact)!;
}
