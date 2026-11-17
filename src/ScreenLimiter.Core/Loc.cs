namespace ScreenLimiter.Core;

/// <summary>All user-facing text, in Hebrew and English. Hebrew is the default.</summary>
public static class Loc
{
    public static string Language { get; set; } = "he";

    public static bool IsRtl => Language == "he";

    public static string T(string key) =>
        Table.TryGetValue(key, out var pair) ? (Language == "he" ? pair.He : pair.En) : key;

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    public static string DayName(int dayOfWeek) => T("day." + dayOfWeek);

    public static string Minutes(int minutes) => T("fmt.min", minutes);

    /// <summary>"+15 min", "+1 h": extra time on offer.</summary>
    public static string Plus(int minutes) => T("fmt.plus", Duration(minutes * 60));

    /// <summary>"today at 16:00", "tomorrow at 00:00", "on Tuesday at 16:00", relative to <paramref name="now"/>.</summary>
    public static string When(DateTime at, DateTime now)
    {
        var time = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var days = (at.Date - now.Date).Days;
        if (days == 0) return T("when.today", time);
        if (days == 1) return T("when.tomorrow", time);
        return T("when.day", DayName((int)at.DayOfWeek), time);
    }

    /// <summary>
    /// "1 h 12 min" / "1 ש׳ 12 דק׳". Time left rounds up so the last minute doesn't read as zero;
    /// time used rounds down, so used + left never adds up to more than the limit.
    /// </summary>
    public static string Duration(int seconds, bool roundUp = true)
    {
        var exact = Math.Max(0, seconds) / 60.0;
        var totalMinutes = (int)(roundUp ? Math.Ceiling(exact) : Math.Floor(exact));
        int h = totalMinutes / 60, m = totalMinutes % 60;
        if (h > 0 && m > 0) return T("fmt.hm", h, m);
        return h > 0 ? T("fmt.h", h) : T("fmt.min", m);
    }

    private static readonly Dictionary<string, (string He, string En)> Table = new()
    {
        ["app.title"] = ("מגביל זמן מסך", "Screen limiter"),

        ["fmt.min"] = ("{0} דק׳", "{0} min"),
        ["fmt.h"] = ("{0} ש׳", "{0} h"),
        ["fmt.hm"] = ("{0} ש׳ {1} דק׳", "{0} h {1} min"),
        // The mark keeps "+15" together on the right in Hebrew.
        ["fmt.plus"] = ("\u200E+{0}", "+{0}"),
        ["when.today"] = ("היום ב-{0}", "today at {0}"),
        ["when.tomorrow"] = ("מחר ב-{0}", "tomorrow at {0}"),
        ["when.day"] = ("ב{0} ב-{1}", "on {0} at {1}"),

        ["day.0"] = ("יום ראשון", "Sunday"),
        ["day.1"] = ("יום שני", "Monday"),
        ["day.2"] = ("יום שלישי", "Tuesday"),
        ["day.3"] = ("יום רביעי", "Wednesday"),
        ["day.4"] = ("יום חמישי", "Thursday"),
        ["day.5"] = ("יום שישי", "Friday"),
        ["day.6"] = ("שבת", "Saturday"),

        // Shared status text
        ["status.left"] = ("זמן שנותר", "Time left"),
        ["status.left.today"] = ("נותר להיום", "Time left today"),
        ["status.left.week"] = ("נותר לשבוע", "Time left this week"),
        ["status.left.short"] = ("נותרו {0}", "{0} left"),
        ["status.unlimited"] = ("ללא הגבלה", "No limit"),
        ["status.timeup"] = ("הזמן נגמר", "Time's up"),
        ["status.noservice"] = ("השירות אינו פועל", "Service isn't running"),
        ["status.usedof"] = ("נוצלו {0} מתוך {1}.", "{0} used of {1}."),

        // Agent: warnings and lock overlay
        ["warn.title"] = ("הזמן עומד להיגמר", "Time's almost up"),
        ["warn.body"] = ("נותרו {0}. כדאי לשמור את העבודה.", "{0} left. Save your work."),
        ["overlay.title"] = ("הזמן נגמר", "Time's up"),
        ["overlay.title.day"] = ("הזמן להיום נגמר", "Time's up for today"),
        ["overlay.title.week"] = ("הזמן לשבוע נגמר", "Time's up for this week"),
        ["overlay.title.hours"] = ("מחוץ לשעות המותרות", "Outside allowed hours"),
        ["overlay.next"] = ("זמן נוסף יתחיל {0}.", "More time starts {0}."),
        ["overlay.next.hours"] = ("אפשר להשתמש שוב {0}.", "Allowed again {0}."),
        ["overlay.body.lock"] = ("אפשר לבקש מההורים זמן נוסף.", "Ask a parent for more time."),
        ["overlay.body.signout"] = ("החשבון ינותק בעוד רגע.", "You'll be signed out in a moment."),
        ["overlay.pin"] = ("קוד הורה", "Parent PIN"),
        ["overlay.add"] = ("הוספת זמן", "Add time"),
        ["overlay.switch"] = ("החלפת משתמש", "Switch user"),
        ["overlay.err.pin"] = ("קוד שגוי. נסו שוב.", "Wrong PIN. Try again."),
        ["overlay.err.locked"] = ("יותר מדי ניסיונות. נסו שוב בעוד כמה דקות.", "Too many attempts. Try again in a few minutes."),
        ["overlay.err.nopin"] = ("עדיין לא הוגדר קוד הורה.", "No parent PIN is set yet."),
        ["overlay.err.empty"] = ("הקלידו את קוד ההורה.", "Enter the parent PIN."),
        ["overlay.err.service"] = ("אין חיבור לשירות. נסו שוב.", "Couldn't reach the service. Try again."),

        // Admin console
        ["admin.users"] = ("משתמשים במחשב", "Local users"),
        ["admin.protection.on"] = ("ההגנה פעילה", "Protection on"),
        ["admin.neverlimited"] = ("מנהל, ללא הגבלה", "Admin, never limited"),
        ["admin.adminnote"] = ("חשבונות מנהל אינם מוגבלים. כדי להגביל משתמש זה, הפכו אותו בהגדרות Windows לחשבון רגיל.",
            "Admin accounts aren't limited. To limit this user, make it a standard account in Windows settings."),
        ["admin.select"] = ("בחרו משתמש מהרשימה", "Select a user from the list"),
        ["admin.language"] = ("שפה", "Language"),
        ["admin.refresh"] = ("רענון", "Refresh"),
        ["admin.badge.active"] = ("הגבלה פעילה", "Limit active"),
        ["admin.badge.timeup"] = ("הזמן נגמר", "Time's up"),
        ["admin.pinset"] = ("קוד הורה מוגדר", "Parent PIN is set"),

        ["mode.none"] = ("ללא הגבלה", "No limit"),
        ["mode.daily"] = ("מכסה יומית", "Daily limit"),
        ["mode.weekly"] = ("מכסה שבועית", "Weekly limit"),
        ["mode.hours"] = ("שעות מותרות", "Allowed hours"),

        ["edit.same"] = ("זהה לכל הימים", "Same for every day"),
        ["edit.everyday"] = ("כל יום", "Every day"),
        ["edit.blocked"] = ("חסום", "Blocked"),
        ["edit.weekly.total"] = ("סך הכול בשבוע", "Per week"),
        ["edit.weekly.cap"] = ("מקסימום ליום", "Max per day"),
        ["edit.weekly.nocap"] = ("ללא", "None"),
        ["edit.weekly.note"] = ("השבוע מתאפס ביום ראשון בחצות. זמן שלא נוצל אינו עובר לשבוע הבא.",
            "The week resets on Sunday at 00:00. Unused time doesn't carry over."),
        ["edit.hours.to"] = ("עד", "to"),
        ["edit.timeup"] = ("כשהזמן נגמר", "When time is up"),
        ["edit.timeup.lock"] = ("נעילת המסך", "Lock the screen"),
        ["edit.timeup.signout"] = ("ניתוק המשתמש", "Sign the user out"),
        ["edit.warn"] = ("התראה לפני הסיום", "Warn before the end"),
        ["edit.warn.15_5"] = ("15 ו-5 דקות לפני", "15 and 5 minutes"),
        ["edit.warn.5"] = ("5 דקות לפני", "5 minutes"),
        ["edit.warn.none"] = ("ללא התראה", "No warning"),
        ["edit.save"] = ("שמירת שינויים", "Save changes"),
        ["edit.discard"] = ("ביטול שינויים", "Discard"),
        ["edit.addtime"] = ("הוספת זמן להיום", "Add time today"),
        ["edit.saved"] = ("נשמר", "Saved"),
        ["edit.added"] = ("נוספו {0}", "Added {0}"),
        ["edit.err.time"] = ("שעה לא תקינה. הקלידו בתבנית 16:00, כשההתחלה אינה אחרי הסיום.",
            "That time isn't valid. Use the form 16:00, with the start no later than the end."),

        ["pin.button"] = ("קוד הורה", "Parent PIN"),
        ["pin.title"] = ("הגדרת קוד הורה", "Set parent PIN"),
        ["pin.help"] = ("הקוד מאפשר להוסיף זמן ממסך הנעילה בלי להיכנס כמנהל.",
            "The PIN adds time from the lock screen without signing in as admin."),
        ["pin.new"] = ("קוד חדש", "New PIN"),
        ["pin.repeat"] = ("הקוד שוב", "Repeat PIN"),
        ["pin.save"] = ("שמירת קוד", "Save PIN"),
        ["pin.err.format"] = ("הקוד צריך להיות 4 עד 8 ספרות.", "The PIN must be 4 to 8 digits."),
        ["pin.err.match"] = ("הקודים אינם זהים.", "The PINs don't match."),
        ["pin.saved"] = ("הקוד נשמר", "PIN saved"),
        ["pin.notset"] = ("לא הוגדר קוד הורה", "No parent PIN set"),

        ["err.service"] = ("אין חיבור לשירות. ודאו שהשירות ScreenLimiter פועל.",
            "Couldn't reach the service. Check that the ScreenLimiter service is running."),
        ["err.notadmin"] = ("נדרשות הרשאות מנהל.", "Administrator rights are required."),
        ["err.save"] = ("השמירה נכשלה. נסו שוב.", "Couldn't save. Try again."),
    };
}
