using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static WinCompanion.Tools.NativeMethods;

namespace WinCompanion.Tools;

internal static class SystemTools
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromMilliseconds(700);

    public static IEnumerable<ITool> All()
    {
        yield return new DelegateTool("get_system_info",
            "Report battery, CPU load, RAM, disk space, Wi-Fi, uptime and the busiest processes. Use it to answer 'why is my PC slow?'.",
            null, [], false, _ => BuildReportAsync());

        yield return new DelegateTool("media_control", "Control media playback with the media keys.",
            new System.Text.Json.Nodes.JsonObject
            {
                ["action"] = ToolSchema.Choice("What to do.", "play_pause", "next", "previous", "stop")
            }, ["action"], false,
            args =>
            {
                var action = args["action"]!.GetValue<string>();
                PressKey(action switch
                {
                    "next" => 0xB0,
                    "previous" => 0xB1,
                    "stop" => 0xB2,
                    _ => 0xB3
                });
                return Task.FromResult($"Sent {action}.");
            });
    }

    private static async Task<string> BuildReportAsync()
    {
        var report = new StringBuilder();

        // CPU: sample total and per-process time across a short window.
        GetSystemTimes(out var idle1, out var kernel1, out var user1);
        var before = SnapshotCpu();
        var clock = Stopwatch.StartNew();
        await Task.Delay(SampleWindow);
        GetSystemTimes(out var idle2, out var kernel2, out var user2);
        var elapsed = clock.Elapsed;

        var total = (kernel2 - kernel1) + (user2 - user1);
        var cpu = total > 0 ? 100.0 * (1.0 - (double)(idle2 - idle1) / total) : 0;
        report.AppendLine($"CPU load: {cpu:0}% across {Environment.ProcessorCount} logical cores");

        // RAM
        var mem = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem))
            report.AppendLine($"RAM: {mem.MemoryLoad}% used ({Gb(mem.TotalPhys - mem.AvailPhys)} of {Gb(mem.TotalPhys)})");

        // Battery
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        report.AppendLine(power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            ? "Battery: none (desktop)"
            : $"Battery: {power.BatteryLifePercent:P0}, {(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "plugged in" : "on battery")}" +
              (power.BatteryLifeRemaining > 0 ? $", about {TimeSpan.FromSeconds(power.BatteryLifeRemaining):h\\h\\ m\\m} left" : ""));

        // Disks
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            report.AppendLine($"Disk {drive.Name}: {Gb(drive.AvailableFreeSpace)} free of {Gb(drive.TotalSize)}");

        report.AppendLine(WifiLine());
        report.AppendLine($"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\d\\ h\\h\\ m\\m}");

        // Busiest processes, grouped by name (browsers run many).
        var after = SnapshotProcesses();
        var cpuByName = after
            .Where(p => before.TryGetValue(p.Key, out _))
            .GroupBy(p => p.Value.Name)
            .Select(g => (Name: g.Key,
                Pct: g.Sum(p => (p.Value.Cpu - before[p.Key].Cpu).TotalMilliseconds) / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100))
            .OrderByDescending(x => x.Pct).Take(5);
        var ramByName = after.GroupBy(p => p.Value.Name)
            .Select(g => (Name: g.Key, Bytes: g.Sum(p => p.Value.Memory)))
            .OrderByDescending(x => x.Bytes).Take(5);

        report.AppendLine("Top CPU: " + string.Join(", ", cpuByName.Select(x => $"{x.Name} {x.Pct:0.#}%")));
        report.Append("Top RAM: " + string.Join(", ", ramByName.Select(x => $"{x.Name} {Gb(x.Bytes)}")));
        return report.ToString();
    }

    private static Dictionary<int, (string Name, TimeSpan Cpu)> SnapshotCpu() =>
        SnapshotProcesses().ToDictionary(p => p.Key, p => (p.Value.Name, p.Value.Cpu));

    private static Dictionary<int, (string Name, TimeSpan Cpu, long Memory)> SnapshotProcesses()
    {
        var result = new Dictionary<int, (string, TimeSpan, long)>();
        foreach (var process in Process.GetProcesses())
        {
            try { result[process.Id] = (process.ProcessName, process.TotalProcessorTime, process.WorkingSet64); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } // protected or exited
            finally { process.Dispose(); }
        }
        return result;
    }

    private static string WifiLine()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { RedirectStandardOutput = true, CreateNoWindow = true };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            var fields = output.Split('\n').Select(l => l.Trim())
                .Where(l => (l.StartsWith("SSID") && !l.StartsWith("BSSID")) || l.StartsWith("Signal") || l.StartsWith("State"))
                .Select(l => Regex.Replace(l, @"\s*:\s*", ": "))
                .ToList();
            return fields.Count == 0 ? "Wi-Fi: no wireless connection" : "Wi-Fi: " + string.Join("; ", fields);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "Wi-Fi: unavailable";
        }
    }

    private static string Gb(double bytes) => $"{bytes / (1024 * 1024 * 1024):0.#} GB";
}
