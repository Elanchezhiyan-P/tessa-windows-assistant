using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>
/// Understands "remind me ..." requests without any AI: "in 10 minutes", "at 5pm", "tomorrow at 9",
/// "on Friday at 14:30", "on 12 Oct at 8am", "tonight", "at noon", and the time before or after the task.
/// </summary>
public static class ReminderParser
{
    public sealed record Parsed(string Message, DateTime Due, string? Repeat = null);

    /// <param name="Reminder">Set when the request was understood.</param>
    /// <param name="Problem">Set when it was a reminder request but needs fixing (no time, or time in the past).</param>
    public sealed record Outcome(Parsed? Reminder, string? Problem);

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const string Months = @"jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";

    private static readonly Regex Trigger = new(
        @"\b(?:(?:set|add|create|make)\s+(?:me\s+)?(?:a\s+|an\s+)?reminder|remind\s+me|reminder)\b[\s:,\-]*", Opts);

    // Only an explicit request ("remind me ...", "set a reminder ...") may be answered with "when?". A passing mention of the
    // word, as in "show my reminder settings", must fall through to the AI instead of being hijacked.
    private static readonly Regex ExplicitRequest = new(
        @"\b(?:(?:set|add|create|make)\s+(?:me\s+)?(?:a\s+|an\s+)?reminder|remind\s+me)\b", Opts);

    private static readonly Regex Relative = new(
        @"\b(?:in|after)\s+(?<n>half\s+an?|an?|\d+(?:\.\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|fifteen|twenty|thirty|forty|sixty)\s*(?<u>seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?)\b", Opts);

    // Recurring: "every day", "daily", "every weekday", "every Monday", "weekly", "every 2 hours", "every hour".
    private static readonly Regex EveryInterval = new(
        @"\bevery\s+(?<n>half\s+an?|\d+(?:\.\d+)?|one|two|three|four|five|six|ten|fifteen|twenty|thirty)?\s*(?<u>minutes?|mins?|hours?|hrs?)\b", Opts);
    private static readonly Regex EveryDay = new(@"\b(?:every\s*day|daily|each\s+day)\b", Opts);
    private static readonly Regex EveryWeekday = new(@"\b(?:every\s+weekday|weekdays|on\s+weekdays|each\s+weekday)\b", Opts);
    private static readonly Regex EveryWeek = new(@"\b(?:every\s+week(?!\s*day)|weekly)\b", Opts);
    private static readonly Regex EveryNamedDay = new(@"\bevery\s+(?=monday|tuesday|wednesday|thursday|friday|saturday|sunday)", Opts);

    // Timers: "set a timer for 25 minutes", "10 minute timer", "timer 5 min for tea".
    private static readonly Regex TimerFor = new(
        @"\btimer\s+(?:for|of)?\s*(?<n>half\s+an?|an?|\d+(?:\.\d+)?|one|two|three|four|five|six|ten|fifteen|twenty|thirty|forty|sixty)\s*[- ]?(?<u>seconds?|secs?|minutes?|mins?|hours?|hrs?)\b", Opts);
    private static readonly Regex DurationTimer = new(
        @"\b(?<n>half\s+an?|an?|\d+(?:\.\d+)?|one|two|three|four|five|six|ten|fifteen|twenty|thirty|forty|sixty)\s*[- ]?(?<u>seconds?|secs?|minutes?|mins?|hours?|hrs?)\s+timer\b", Opts);

    /// <summary>"set a timer for 25 minutes" is a reminder that fires in that long. Null when the text isn't a timer request.</summary>
    public static Parsed? TryTimer(string input, DateTime now)
    {
        var m = TimerFor.Match(input);
        if (!m.Success) m = DurationTimer.Match(input);
        if (!m.Success) return null;
        var amount = ParseNumber(m.Groups["n"].Value);
        var unit = m.Groups["u"].Value.ToLowerInvariant();
        var span = unit[0] switch { 's' => TimeSpan.FromSeconds(amount), 'm' => TimeSpan.FromMinutes(amount), _ => TimeSpan.FromHours(amount) };
        if (span < TimeSpan.FromSeconds(1)) return null;
        var rest = input[..m.Index] + " " + input[(m.Index + m.Length)..];
        rest = Regex.Replace(rest, @"\b(?:please|set|start|create|make|a|an|my|the|timer|for|called|named|to|me)\b", " ", RegexOptions.IgnoreCase);
        var label = CleanMessage(rest);
        var message = label == "Reminder" ? "Your timer is done" : $"Timer: {label}";
        return new Parsed(message, now + span);
    }

    private static readonly Regex Day = new(
        @"\b(?<w>today|tonight|tomorrow|day\s+after\s+tomorrow|(?:(?<mod>on|next|this|coming)\s+)?(?<wd>monday|tuesday|wednesday|thursday|friday|saturday|sunday))\b", Opts);

    private static readonly Regex IsoDate = new(@"\b(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})\b", Opts);
    private static readonly Regex DayMonth = new($@"\b(?:on\s+)?(?<d>\d{{1,2}})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<mon>{Months})\b", Opts);
    private static readonly Regex MonthDay = new($@"\b(?:on\s+)?(?<mon>{Months})\s+(?<d>\d{{1,2}})(?:st|nd|rd|th)?\b", Opts);

    private static readonly Regex Clock = new(
        @"(?<![\d:.])(?<at>\b(?:at|@)\s*)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>a\.?m\.?|p\.?m\.?)?(?![\d:])", Opts);

    private static readonly Regex Named = new(@"\b(?:at\s+)?(?<n>noon|midday|midnight)\b", Opts);
    private static readonly Regex PartOfDay = new(@"\b(?:this\s+|in\s+the\s+)?(?<p>morning|afternoon|evening|night)\b", Opts);

    private static readonly Regex LeadingFiller = new(@"^(?:(?:to|that|about|of|for|me|please)\s+)+", Opts);
    private static readonly Regex TrailingFiller = new(@"(?:\s+(?:at|on|in|by|for|around|before|after|this|next|coming|the|and))+$", Opts);

    /// <summary>Returns null when the text isn't a reminder request at all.</summary>
    public static Outcome? Parse(string input, DateTime now)
    {
        var trigger = Trigger.Match(input);
        if (!trigger.Success) return null;

        // Work on a copy where each recognised piece is blanked out; what remains is the task text.
        var work = new StringBuilder(input);
        Blank(work, trigger);

        // Recurrence words are noted and blanked so the rest of the parsing sees an ordinary reminder.
        string? repeat = null;
        DateTime? intervalDue = null;
        if (EveryInterval.Match(work.ToString()) is { Success: true } iv)
        {
            var n = iv.Groups["n"].Success ? ParseNumber(iv.Groups["n"].Value) : 1;
            var minutes = (int)Math.Round(iv.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? n * 60 : n);
            if (minutes >= 1) { repeat = $"every:{minutes}"; intervalDue = now.AddMinutes(minutes); Blank(work, iv); }
        }
        else if (EveryWeekday.Match(work.ToString()) is { Success: true } wd) { repeat = "weekdays"; Blank(work, wd); }
        else if (EveryDay.Match(work.ToString()) is { Success: true } ed) { repeat = "daily"; Blank(work, ed); }
        else if (EveryNamedDay.Match(work.ToString()) is { Success: true } en) { repeat = "weekly"; Blank(work, en); }
        else if (EveryWeek.Match(work.ToString()) is { Success: true } ew) { repeat = "weekly"; Blank(work, ew); }

        if (intervalDue is not null)
            return new Outcome(new Parsed(CleanMessage(work.ToString()), intervalDue.Value, repeat), null);

        string? problem = null;
        var due = TryRelative(work, now);
        due ??= TryAbsolute(work, now, out problem);
        if (due is null)
        {
            if (problem is not null) return new Outcome(null, problem);
            return LooksLikeQuestion(input) || !ExplicitRequest.IsMatch(input)
                ? null // e.g. "remind me what the capital of France is" is for the AI, not a timer
                : new Outcome(null, "When should I remind you? Try “in 10 minutes”, “at 5pm” or “tomorrow at 9am”.");
        }

        if (repeat == "weekdays")
            while (due.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) due = due.Value.AddDays(1);
        return new Outcome(new Parsed(CleanMessage(work.ToString()), due.Value, repeat), null);
    }

    // ---- Relative: "in 10 minutes" -------------------------------------------------------------

    private static DateTime? TryRelative(StringBuilder work, DateTime now)
    {
        var m = Relative.Match(work.ToString());
        if (!m.Success) return null;

        var amount = ParseNumber(m.Groups["n"].Value);
        var unit = m.Groups["u"].Value.ToLowerInvariant();
        var span = unit[0] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromDays(7 * amount) // weeks
        };
        Blank(work, m);
        return now + span;
    }

    private static double ParseNumber(string text)
    {
        text = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ");
        if (text.StartsWith("half")) return 0.5;
        return text switch
        {
            "a" or "an" or "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5, "six" => 6,
            "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10, "fifteen" => 15, "twenty" => 20,
            "thirty" => 30, "forty" => 40, "sixty" => 60,
            _ => double.Parse(text, CultureInfo.InvariantCulture)
        };
    }

    // ---- Absolute: day/date + time of day -------------------------------------------------------

    private static DateTime? TryAbsolute(StringBuilder work, DateTime now, out string? problem)
    {
        problem = null;
        var today = now.Date;
        DateTime? date = null;
        var rollWeekday = false;      // "friday" said on a Friday: today if still ahead, else next week
        var eveningWord = false;      // "tonight", "this evening": bias an ambiguous hour to the evening

        // Date first (so "12th oct" isn't mistaken for a clock time).
        if (IsoDate.Match(work.ToString()) is { Success: true } iso &&
            TryDate(int.Parse(iso.Groups["y"].Value), int.Parse(iso.Groups["m"].Value), int.Parse(iso.Groups["d"].Value), out var isoDate))
        {
            date = isoDate;
            Blank(work, iso);
        }
        else if ((DayMonth.Match(work.ToString()) is { Success: true } dm ? dm : MonthDay.Match(work.ToString())) is { Success: true } md)
        {
            var month = MonthNumber(md.Groups["mon"].Value);
            var day = int.Parse(md.Groups["d"].Value);
            if (TryDate(today.Year, month, day, out var d))
            {
                date = d < today ? d.AddYears(1) : d;
                Blank(work, md);
            }
        }

        if (date is null && Day.Match(work.ToString()) is { Success: true } dayMatch)
        {
            var word = dayMatch.Groups["w"].Value.ToLowerInvariant();
            if (dayMatch.Groups["wd"].Success)
            {
                var target = Enum.Parse<DayOfWeek>(dayMatch.Groups["wd"].Value, ignoreCase: true);
                var diff = ((int)target - (int)today.DayOfWeek + 7) % 7;
                var next = dayMatch.Groups["mod"].Value.Equals("next", StringComparison.OrdinalIgnoreCase);
                if (diff == 0 && next) diff = 7;
                rollWeekday = diff == 0 && !next;
                date = today.AddDays(diff);
            }
            else if (word.StartsWith("tomorrow")) date = today.AddDays(1);
            else if (word.StartsWith("day")) date = today.AddDays(2);
            else date = today; // today / tonight
            eveningWord = word == "tonight";
            Blank(work, dayMatch);
        }

        // Time of day.
        TimeSpan? time = null;
        var ambiguousHour = -1;
        var minute = 0;

        var rest = work.ToString();
        var named = Named.Match(rest);
        var clock = FindClock(rest);

        string? partOfDay = null;
        if (PartOfDay.Match(rest) is { Success: true } pod) partOfDay = pod.Groups["p"].Value.ToLowerInvariant();

        if (named.Success)
        {
            time = named.Groups["n"].Value.Equals("midnight", StringComparison.OrdinalIgnoreCase) ? TimeSpan.Zero : TimeSpan.FromHours(12);
            Blank(work, named);
        }
        else if (clock is not null)
        {
            var hour = int.Parse(clock.Groups["h"].Value);
            minute = clock.Groups["m"].Success ? int.Parse(clock.Groups["m"].Value) : 0;
            var ap = clock.Groups["ap"].Value.ToLowerInvariant();

            if (ap.StartsWith('p')) time = TimeSpan.FromHours(hour % 12 + 12);
            else if (ap.StartsWith('a')) time = TimeSpan.FromHours(hour % 12);
            else if (hour is 0 or >= 13) time = TimeSpan.FromHours(hour);
            else if (eveningWord || partOfDay is "afternoon" or "evening" or "night") time = TimeSpan.FromHours(hour == 12 ? 12 : hour + 12);
            else if (partOfDay == "morning") time = TimeSpan.FromHours(hour % 12);
            else ambiguousHour = hour; // "at 5": decided below once the date is known

            if (time is not null) time += TimeSpan.FromMinutes(minute);
            Blank(work, clock);
            if (partOfDay is not null && PartOfDay.Match(work.ToString()) is { Success: true } used) Blank(work, used);
        }
        else if (partOfDay is not null)
        {
            time = TimeSpan.FromHours(partOfDay switch { "morning" => 9, "afternoon" => 15, "evening" => 18, _ => 21 });
            Blank(work, PartOfDay.Match(work.ToString()));
        }
        else if (eveningWord)
        {
            time = TimeSpan.FromHours(20);
        }

        if (date is null && time is null && ambiguousHour < 0) return null; // no time information at all

        // Resolve "at 5" with no am/pm.
        if (ambiguousHour >= 0)
        {
            var am = TimeSpan.FromHours(ambiguousHour % 12) + TimeSpan.FromMinutes(minute);
            var pm = am + TimeSpan.FromHours(12);
            if (date is null)
            {
                // Pick the next occurrence, skipping the small hours ("at 3" means 3 PM, not 3 AM).
                var candidates = new[] { today + am, today + pm, today.AddDays(1) + am, today.AddDays(1) + pm }
                    .Where(c => c > now).ToList();
                return candidates.FirstOrDefault(c => c.Hour >= 6) is var awake && awake != default ? awake : candidates[0];
            }
            time = ambiguousHour is >= 1 and <= 6 or 12 ? pm : am; // 1-6 -> afternoon/evening, 7-11 -> morning
        }

        time ??= TimeSpan.FromHours(9); // a day with no time: default to 9 AM

        DateTime due;
        if (date is null)
        {
            due = today + time.Value;
            if (due <= now) due = due.AddDays(1);   // "at 9am" said at 10am means tomorrow
        }
        else
        {
            due = date.Value + time.Value;
            if (due <= now && rollWeekday) due = due.AddDays(7);
        }

        if (due <= now)
        {
            problem = "That time has already passed. Pick a time in the future.";
            return null;
        }
        return due;
    }

    // A bare number only counts as a clock time with "at", am/pm, or a colon (so "buy 2 apples" is left alone).
    private static Match? FindClock(string text)
    {
        foreach (Match m in Clock.Matches(text))
        {
            var hour = int.Parse(m.Groups["h"].Value);
            var hasAp = m.Groups["ap"].Success && m.Groups["ap"].Value.Length > 0;
            if (!(m.Groups["at"].Success || hasAp || m.Groups["m"].Success)) continue;
            if (hasAp ? hour is < 1 or > 12 : hour > 23) continue;
            if (m.Groups["m"].Success && int.Parse(m.Groups["m"].Value) > 59) continue;
            return m;
        }
        return null;
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private static bool TryDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateTime(year, month, day);
        return true;
    }

    private static int MonthNumber(string text) =>
        DateTime.ParseExact(text[..3], "MMM", CultureInfo.InvariantCulture).Month;

    private static void Blank(StringBuilder text, Match match)
    {
        if (!match.Success) return;
        for (var i = 0; i < match.Length; i++) text[match.Index + i] = ' ';
    }

    private static bool LooksLikeQuestion(string text) =>
        text.Contains('?') || Regex.IsMatch(text, @"\bremind\s+me\s+(?:what|who|how|why|when|where|which)\b", Opts);

    private static string CleanMessage(string text)
    {
        var message = Regex.Replace(text, @"\s+", " ").Trim();
        message = LeadingFiller.Replace(message, "");
        message = TrailingFiller.Replace(message, "");
        message = message.Trim(' ', ',', '.', '-', ':', ';');
        if (message.Length == 0) return "Reminder";
        return char.ToUpperInvariant(message[0]) + message[1..];
    }
}
