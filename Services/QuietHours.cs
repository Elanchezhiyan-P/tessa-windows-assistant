using System.Globalization;

namespace WinCompanion.Services;

/// <summary>A daily window (such as 22:00 to 07:00) during which she keeps quiet. The window may cross midnight.</summary>
public static class QuietHours
{
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "%H", "h:mm tt", "h tt", "h:mmtt", "htt"];

    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!DateTime.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return false;
        time = parsed.TimeOfDay;
        return true;
    }

    /// <summary>Reads "22:00-07:00", "22:00 to 7:00" or "10pm 7am".</summary>
    public static bool TryParseRange(string? text, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normal = text.Replace(" to ", "-", StringComparison.OrdinalIgnoreCase).Replace(" until ", "-", StringComparison.OrdinalIgnoreCase);
        var parts = normal.Split(['-', '–', '—'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            parts = normal.Split(' ', StringSplitOptions.RemoveEmptyEntries); // "22:00 07:00"
            if (parts.Length != 2) return false;
        }
        return TryParseTime(parts[0], out start) && TryParseTime(parts[1], out end);
    }

    /// <summary>True when <paramref name="now"/> falls in [start, end). Equal times mean no quiet period.</summary>
    public static bool IsQuiet(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        if (start == end) return false;
        return start < end ? now >= start && now < end : now >= start || now < end;
    }

    public static bool IsQuietNow(AppSettings settings, DateTime now) =>
        settings.QuietHours
        && TryParseTime(settings.QuietStart, out var start)
        && TryParseTime(settings.QuietEnd, out var end)
        && IsQuiet(now.TimeOfDay, start, end);

    public static string Format(TimeSpan time) => DateTime.Today.Add(time).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Describe(AppSettings settings) =>
        settings.QuietHours
            ? $"Quiet hours are on, {settings.QuietStart} to {settings.QuietEnd}. Nudges and warnings stay silent then{(settings.QuietOwnReminders ? ", and so do your own reminders" : "; your own reminders still sound")}."
            : "Quiet hours are off. Set them with /quiet 22:00-07:00.";
}
