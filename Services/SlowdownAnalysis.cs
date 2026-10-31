using System.Text;

namespace WinCompanion.Services;

public sealed record ProcessLoad(string Name, double CpuPercent);

public sealed record ProcessMemory(string Name, long Bytes);

/// <param name="StartupOn">How many programs are set to start with Windows.</param>
/// <param name="TempBytes">Junk that a cleanup could remove.</param>
public sealed record SlowdownInputs(
    double CpuPercent, int RamPercent, IReadOnlyList<ProcessLoad> TopCpu, IReadOnlyList<ProcessMemory> TopRam,
    IReadOnlyList<DriveSpec> Drives, int StartupOn, TimeSpan Uptime, long TempBytes);

/// <param name="Severity">3 is a likely cause, 2 is worth a look, 1 is only information.</param>
public sealed record Finding(int Severity, string Text, string? Fix);

/// <summary>Turns a few measurements of the PC into a short, ranked list of likely reasons it feels slow, each with something to do about it.</summary>
public static class SlowdownAnalysis
{
    private const long Gb = 1024L * 1024 * 1024;

    public static IReadOnlyList<Finding> Analyze(SlowdownInputs i)
    {
        var found = new List<Finding>();

        var hog = i.TopCpu.OrderByDescending(p => p.CpuPercent).FirstOrDefault();
        if (hog is not null && hog.CpuPercent >= 25 && i.CpuPercent >= 50)
            found.Add(new(3, $"{hog.Name} is using about {hog.CpuPercent:0}% of the processor.",
                $"If you aren't using {hog.Name}, close it. If it keeps doing this, restart it or check for an update."));
        else if (i.CpuPercent >= 80)
            found.Add(new(3, $"The processor is {i.CpuPercent:0}% busy, spread across many programs.", "Close what you aren't using."));
        else if (hog is not null && hog.CpuPercent >= 40)
            found.Add(new(2, $"{hog.Name} is using {hog.CpuPercent:0}% of the processor.", null));

        if (i.RamPercent >= 85)
        {
            var biggest = string.Join(", ", i.TopRam.Take(3).Select(p => $"{p.Name} ({SystemInfo.FormatBytes(p.Bytes)})"));
            found.Add(new(i.RamPercent >= 92 ? 3 : 2, $"Memory is {i.RamPercent}% full. The biggest users are {biggest}.",
                "Close programs and browser tabs you aren't using; Windows slows down a lot when memory runs out."));
        }

        foreach (var drive in i.Drives)
        {
            if (drive.FreeBytes < HealthRules.DiskThreshold(drive.TotalBytes))
                found.Add(new(drive.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? 3 : 2,
                    $"Drive {drive.Name} is nearly full: {SystemInfo.FormatBytes(drive.FreeBytes)} free of {SystemInfo.FormatBytes(drive.TotalBytes)}.",
                    "Windows needs free space to work. Run /cleanup, then uninstall or move things you don't need."));
        }

        if (i.StartupOn >= 10)
            found.Add(new(i.StartupOn >= 15 ? 3 : 2, $"{i.StartupOn} programs start with Windows, which slows start-up and uses memory all day.",
                "Type /startup to see them and switch off the ones you don't need."));

        if (i.Uptime >= TimeSpan.FromDays(7))
            found.Add(new(2, $"This PC hasn't restarted for {i.Uptime.TotalDays:0} days.", "A restart clears memory leaks and finishes pending updates."));

        if (i.TempBytes >= Gb)
            found.Add(new(2, $"{SystemInfo.FormatBytes(i.TempBytes)} of temporary files are waiting to be cleaned.", "Run /cleanup."));

        if (found.Count == 0)
            found.Add(new(1, $"Nothing stands out: the processor is {i.CpuPercent:0}% busy and memory is {i.RamPercent}% full.",
                "If it still feels slow, tell me what is slow (starting up, a certain program, the browser) and I'll look closer."));

        return found.OrderByDescending(f => f.Severity).ToList();
    }

    public static string Describe(SlowdownInputs i, IReadOnlyList<Finding> findings)
    {
        var sb = new StringBuilder($"Right now: processor {i.CpuPercent:0}%, memory {i.RamPercent}% used, up for {FormatUptime(i.Uptime)}.");
        foreach (var f in findings)
        {
            // Each finding is its own short block: the cause in bold, then what to do about it.
            sb.Append("\n\n**").Append(f.Severity switch { 3 => "⚠ ", 2 => "• ", _ => "ℹ " }).Append(f.Text).Append("**");
            if (f.Fix is not null) sb.Append("\n→ ").Append(f.Fix);
        }
        return sb.ToString();
    }

    public static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1 ? $"{uptime.Days} day(s) {uptime.Hours} h" : $"{uptime.Hours} h {uptime.Minutes} min";
}
