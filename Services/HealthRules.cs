namespace WinCompanion.Services;

/// <summary>
/// Decides when a low-disk or low-battery warning is due. It remembers what it already warned about, so each problem is
/// reported once and again only after it has gone away and come back.
/// </summary>
public sealed class HealthRules
{
    private const long Gb = 1024L * 1024 * 1024;
    public const int LowBattery = 20;
    public const int CriticalBattery = 10;
    public const int RecoveredBattery = 30;

    private readonly HashSet<string> _warnedDrives = new(StringComparer.OrdinalIgnoreCase);
    private int _batteryWarnedAt; // 0 none, else the level (LowBattery or CriticalBattery) already reported

    /// <summary>Free space below which a drive counts as low: 5% of it, but never under 10 GB or over 30 GB.</summary>
    public static long DiskThreshold(long totalBytes) => (long)Math.Clamp(totalBytes * 0.05, 10 * Gb, 30 * Gb);

    /// <param name="batteryPercent">Null when there is no battery.</param>
    /// <param name="onBattery">True when running on battery rather than plugged in.</param>
    public IReadOnlyList<string> Check(IEnumerable<DriveSpec> drives, int? batteryPercent, bool onBattery)
    {
        var warnings = new List<string>();

        foreach (var drive in drives)
        {
            var threshold = DiskThreshold(drive.TotalBytes);
            if (drive.FreeBytes < threshold)
            {
                if (_warnedDrives.Add(drive.Name))
                    warnings.Add($"Drive {drive.Name} is running low on space: {SystemInfo.FormatBytes(drive.FreeBytes)} free of {SystemInfo.FormatBytes(drive.TotalBytes)}.");
            }
            else if (drive.FreeBytes > threshold * 1.2)
            {
                _warnedDrives.Remove(drive.Name); // recovered with some margin, so warn again if it fills up once more
            }
        }

        if (batteryPercent is { } percent)
        {
            if (!onBattery || percent >= RecoveredBattery) _batteryWarnedAt = 0;
            else if (percent <= CriticalBattery && _batteryWarnedAt != CriticalBattery)
            {
                _batteryWarnedAt = CriticalBattery;
                warnings.Add($"The battery is critically low ({percent}%). Plug in now or save your work.");
            }
            else if (percent <= LowBattery && _batteryWarnedAt == 0)
            {
                _batteryWarnedAt = LowBattery;
                warnings.Add($"The battery is at {percent}%. Plug in soon.");
            }
        }

        return warnings;
    }

    /// <summary>The battery level (null on a desktop PC) and whether the PC is running on it.</summary>
    public static (int? Percent, bool OnBattery) ReadBattery()
    {
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            || power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Unknown))
            return (null, false);

        var life = power.BatteryLifePercent; // 0 to 1; anything else means Windows doesn't know
        if (life is < 0 or > 1) return (null, false);
        return ((int)Math.Round(life * 100), power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline);
    }

    /// <summary>What the PC looks like right now, for /health.</summary>
    public static string Describe(IEnumerable<DriveSpec> drives, int? batteryPercent, bool onBattery, bool alertsOn)
    {
        var lines = drives.Select(d =>
            $"Drive {d.Name} {SystemInfo.FormatBytes(d.FreeBytes)} free of {SystemInfo.FormatBytes(d.TotalBytes)}" +
            (d.FreeBytes < DiskThreshold(d.TotalBytes) ? " (low)" : "")).ToList();
        lines.Add(batteryPercent is { } p
            ? $"Battery: {p}%, {(onBattery ? "on battery" : "plugged in")}"
            : "Battery: none (a desktop PC)");
        lines.Add(alertsOn ? "Warnings are on: I'll tell you when a drive or the battery runs low." : "Warnings are off (Settings, Look and alerts).");
        return string.Join('\n', lines);
    }
}
