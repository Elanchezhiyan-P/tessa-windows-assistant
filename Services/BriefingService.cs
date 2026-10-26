using System.Text;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>
/// The daily briefing: a greeting, today's reminders, the weather and the headlines for the topics you chose.
/// It is built from the same reminders, weather and web lookup the assistant already uses, with no AI model involved,
/// so it works with a local model and costs no Gemini quota.
/// </summary>
public static class BriefingService
{
    public static string Greeting(DateTime now, string? name)
    {
        var part = now.Hour switch { < 5 => "Hello", < 12 => "Good morning", < 17 => "Good afternoon", _ => "Good evening" };
        return string.IsNullOrWhiteSpace(name) ? part : $"{part}, {name}";
    }

    /// <summary>Lays the briefing out as Markdown. Pure, so it can be tested without a network.</summary>
    public static string Compose(DateTime now, string? name, IReadOnlyList<Reminder> todaysReminders, string? weather,
        IReadOnlyList<(string Topic, List<(string Title, string Url)> Items)> news)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**{Greeting(now, name)}!** Here is your briefing for {now:dddd, d MMMM}.").AppendLine();

        sb.AppendLine("**Today's reminders**");
        if (todaysReminders.Count == 0) sb.AppendLine("- Nothing scheduled.");
        foreach (var r in todaysReminders)
            sb.AppendLine($"- {r.Due:h:mm tt}: {r.Message}{(r.RepeatText is { } t ? $" (repeats {t})" : "")}");

        if (!string.IsNullOrWhiteSpace(weather))
        {
            sb.AppendLine().AppendLine("**Weather**");
            sb.AppendLine(weather.Trim());
        }

        foreach (var (topic, items) in news)
        {
            if (items.Count == 0) continue;
            sb.AppendLine().AppendLine($"**{topic}**");
            foreach (var (title, url) in items) sb.AppendLine($"- [{title}]({url})");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Fetches the weather and headlines (in parallel) and composes the briefing.</summary>
    public static async Task<string> BuildAsync(ReminderStore reminders, AppSettings settings, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var today = reminders.Pending().Where(r => r.Due.Date == t.Date && r.Due >= t).ToList();

        var topics = settings.BriefingTopics.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4).ToList();
        var weatherTask = EverydayTools.WeatherAsync(settings.BriefingCity.Trim());
        var newsTasks = topics.Select(async topic => (Topic: topic, Items: await EverydayTools.HeadlinesAsync(topic))).ToList();

        var weather = await weatherTask;
        var news = new List<(string, List<(string, string)>)>();
        foreach (var task in newsTasks) news.Add(await task);

        return Compose(t, settings.CallUser, today, weather, news);
    }
}
