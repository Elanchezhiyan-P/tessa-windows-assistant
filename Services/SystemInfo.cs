using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinCompanion.Services;

public sealed record GpuSpec(string Name, long VramBytes);

public sealed record DriveSpec(string Name, long TotalBytes, long FreeBytes);

/// <param name="L1Bytes">Instruction plus data cache of every core, added up. 0 when Windows doesn't say.</param>
/// <param name="L2Bytes">All L2 caches added up.</param>
/// <param name="L3Bytes">All L3 caches added up.</param>
public sealed record SystemSpecs(
    string Os, string Cpu, int Cores, int Threads, long L1Bytes, long L2Bytes, long L3Bytes,
    long RamTotalBytes, long RamFreeBytes, IReadOnlyList<GpuSpec> Gpus, IReadOnlyList<DriveSpec> Drives);

/// <summary>
/// What this PC is made of: memory, processor and its caches, graphics card, disks and Windows version. Read from Windows only,
/// never sent anywhere, and without serial numbers, addresses or user names.
/// </summary>
public static class SystemInfo
{
    public static SystemSpecs Collect()
    {
        var (ramTotal, ramFree) = ReadMemory();
        var (cores, l1, l2, l3) = ReadProcessorTopology();
        return new SystemSpecs(
            ReadOs(), ReadCpuName(), cores > 0 ? cores : Environment.ProcessorCount, Environment.ProcessorCount,
            l1, l2, l3, ramTotal, ramFree, ReadGpus(), ReadDrives());
    }

    /// <summary>The best graphics card to run a model on: the one with the most dedicated memory.</summary>
    public static GpuSpec? MainGpu(SystemSpecs specs) => specs.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();

    // ---- Text -------------------------------------------------------------------------------------------

    public static string FormatBytes(long bytes)
    {
        const double Gb = 1024.0 * 1024 * 1024;
        if (bytes >= Gb) return $"{bytes / Gb:0.#} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes} B";
    }

    /// <summary>The lines shown in Settings and by /system, without the model advice.</summary>
    public static string Describe(SystemSpecs s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Windows: {s.Os}");
        sb.AppendLine($"Processor: {s.Cpu} ({s.Cores} cores, {s.Threads} threads)");
        var caches = new List<string>();
        if (s.L1Bytes > 0) caches.Add($"L1 {FormatBytes(s.L1Bytes)}");
        if (s.L2Bytes > 0) caches.Add($"L2 {FormatBytes(s.L2Bytes)}");
        if (s.L3Bytes > 0) caches.Add($"L3 {FormatBytes(s.L3Bytes)}");
        if (caches.Count > 0) sb.AppendLine($"Cache: {string.Join(" · ", caches)}");
        sb.AppendLine($"Memory: {FormatBytes(s.RamTotalBytes)} total, {FormatBytes(s.RamFreeBytes)} free now");
        foreach (var gpu in s.Gpus)
            sb.AppendLine($"Graphics: {gpu.Name}{(gpu.VramBytes > 0 ? $" ({FormatBytes(gpu.VramBytes)})" : " (shares system memory)")}");
        foreach (var drive in s.Drives)
            sb.AppendLine($"Drive {drive.Name} {FormatBytes(drive.TotalBytes)}, {FormatBytes(drive.FreeBytes)} free");
        return sb.ToString().TrimEnd();
    }

    // ---- Reading Windows --------------------------------------------------------------------------------

    private static string ReadOs()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var b) ? b : 0;
            // The registry still says "Windows 10" on Windows 11.
            var name = build >= 22000 ? "Windows 11" : (key?.GetValue("ProductName") as string ?? "Windows");
            name = name.Replace("Windows 10", build >= 22000 ? "Windows 11" : "Windows 10");
            var version = key?.GetValue("DisplayVersion") as string;
            return $"{name}{(version is null ? "" : " " + version)}{(build > 0 ? $" (build {build})" : "")}, {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0)
                return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        return "Unknown processor";
    }

    private static List<GpuSpec> ReadGpus()
    {
        var list = new List<GpuSpec>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var name in classKey?.GetSubKeyNames() ?? [])
            {
                if (!name.All(char.IsDigit)) continue;
                using var adapter = classKey!.OpenSubKey(name);
                if (adapter?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;

                // The 64-bit value is right for cards over 4 GB; WMI caps at 4 GB.
                var vram = adapter.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] { Length: >= 8 } bytes => BitConverter.ToInt64(bytes, 0),
                    _ => 0L
                };
                if (!list.Any(g => g.Name == desc)) list.Add(new GpuSpec(desc, vram));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        return list;
    }

    public static List<DriveSpec> ReadDrives()
    {
        var list = new List<DriveSpec>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    list.Add(new DriveSpec(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (IOException) { }
        }
        return list;
    }

    // ---- Native -----------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint length);

    private static (long Total, long Free) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)status.TotalPhys, (long)status.AvailPhys) : (0, 0);
    }

    /// <summary>Physical core count and the sizes of all caches, from Windows' logical processor list.</summary>
    private static (int Cores, long L1, long L2, long L3) ReadProcessorTopology()
    {
        // Each entry: a processor mask, a relationship (0 = core, 2 = cache) and 16 bytes of details. For a cache the details are
        // level (1 byte), associativity (1), line size (2), size in bytes (4) and type (4).
        var entrySize = IntPtr.Size * 2 + 16;
        uint length = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref length);
        if (length == 0) return (0, 0, 0, 0);

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref length)) return (0, 0, 0, 0);

            int cores = 0;
            long l1 = 0, l2 = 0, l3 = 0;
            for (var offset = 0; offset + entrySize <= length; offset += entrySize)
            {
                var relationship = Marshal.ReadInt32(buffer, offset + IntPtr.Size);
                var details = offset + IntPtr.Size * 2;
                if (relationship == 0) cores++;
                else if (relationship == 2)
                {
                    var level = Marshal.ReadByte(buffer, details);
                    var size = (uint)Marshal.ReadInt32(buffer, details + 4);
                    switch (level)
                    {
                        case 1: l1 += size; break;
                        case 2: l2 += size; break;
                        case 3: l3 += size; break;
                    }
                }
            }
            return (cores, l1, l2, l3);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
