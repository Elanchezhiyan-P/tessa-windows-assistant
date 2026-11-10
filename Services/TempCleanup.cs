using System.Text;

namespace WinCompanion.Services;

/// <param name="Key">What the user or model names it by: "temp", "cache", "dumps", "reports".</param>
public sealed record CleanupArea(string Key, string Name, string Path);

/// <param name="Files">Files old enough to be removed.</param>
public sealed record CleanupFound(CleanupArea Area, int Files, long Bytes);

/// <param name="Failed">Files that couldn't be removed, usually because a program is using them.</param>
public sealed record CleanupResult(int Deleted, long Bytes, int Failed);

/// <summary>
/// Finds and removes junk in a few folders that only ever hold throwaway files: the user's temp folder, the internet cache,
/// crash dumps and error reports. It touches nothing else, leaves files from the last day alone (a running program may be using
/// them), and never follows a link out of the folder. Deleted files can't be recovered, so the caller asks first.
/// </summary>
public static class TempCleanup
{
    /// <summary>Files newer than this are kept.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    public static IReadOnlyList<CleanupArea> DefaultAreas()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            new("temp", "Temporary files", System.IO.Path.GetTempPath()),
            new("cache", "Internet cache", System.IO.Path.Combine(local, "Microsoft", "Windows", "INetCache")),
            new("dumps", "Crash dumps", System.IO.Path.Combine(local, "CrashDumps")),
            new("reports", "Error reports", System.IO.Path.Combine(local, "Microsoft", "Windows", "WER"))
        ];
    }

    /// <summary>
    /// A folder is only cleaned if it is one of the places that hold nothing but throwaway files: the temp folder (when it really is
    /// called temp) or a folder inside LocalAppData (the internet cache, crash dumps, error reports). Never a drive root, the profile,
    /// Documents, Downloads or Windows, even if an environment variable points there.
    /// </summary>
    public static bool IsSafeRoot(string path)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var profile = System.IO.Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var local = System.IO.Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var temp = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));

        if (full.Length <= 3) return false; // a drive root
        if (full.Equals(profile, StringComparison.OrdinalIgnoreCase)) return false;

        // Anything in the temp folder counts only if that folder is called temp: someone may have pointed TEMP at a folder of real
        // data (even one inside their profile), and that must never be treated as junk.
        if (full.Equals(temp, StringComparison.OrdinalIgnoreCase) || Inside(full, temp))
        {
            var tempName = System.IO.Path.GetFileName(temp);
            return tempName.Contains("temp", StringComparison.OrdinalIgnoreCase) || tempName.Contains("tmp", StringComparison.OrdinalIgnoreCase);
        }

        return Inside(full, local);
    }

    private static bool Inside(string path, string parent) =>
        parent.Length > 0 && path.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // ---- Scanning -----------------------------------------------------------------------------------------

    /// <summary>Counts what a cleanup would remove, without removing anything.</summary>
    public static IReadOnlyList<CleanupFound> Scan(IEnumerable<CleanupArea>? areas = null, DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.Now) - MinAge;
        var found = new List<CleanupFound>();
        foreach (var area in Distinct(areas ?? DefaultAreas()))
        {
            int files = 0;
            long bytes = 0;
            foreach (var file in OldFiles(area.Path, cutoff))
            {
                files++;
                bytes += file.Length;
            }
            found.Add(new CleanupFound(area, files, bytes));
        }
        return found;
    }

    // ---- Cleaning -----------------------------------------------------------------------------------------

    public static CleanupResult Clean(IEnumerable<CleanupArea>? areas = null, DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.Now) - MinAge;
        int deleted = 0, failed = 0;
        long bytes = 0;

        foreach (var area in Distinct(areas ?? DefaultAreas()))
        {
            foreach (var file in OldFiles(area.Path, cutoff).ToList())
            {
                try
                {
                    var length = file.Length;
                    file.IsReadOnly = false;
                    file.Delete();
                    deleted++;
                    bytes += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
            }
            RemoveEmptyFolders(area.Path, cutoff);
        }
        return new CleanupResult(deleted, bytes, failed);
    }

    /// <summary>Files under <paramref name="root"/> last changed before <paramref name="cutoff"/>. Links and unsafe roots are skipped.</summary>
    private static IEnumerable<FileInfo> OldFiles(string root, DateTime cutoff)
    {
        if (!IsSafeRoot(root) || !Directory.Exists(root)) yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(folder).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var child in children)
            {
                FileSystemInfo info;
                try
                {
                    info = Directory.Exists(child) ? new DirectoryInfo(child) : new FileInfo(child);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; // a link could lead anywhere
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                if (info is DirectoryInfo) pending.Push(child);
                else if (info is FileInfo file && Newest(file) < cutoff) yield return file;
            }
        }
    }

    private static DateTime Newest(FileInfo file) =>
        file.LastWriteTime > file.CreationTime ? file.LastWriteTime : file.CreationTime;

    private static void RemoveEmptyFolders(string root, DateTime cutoff)
    {
        if (!IsSafeRoot(root) || !Directory.Exists(root)) return;
        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(f => f.Length).ToList()) // deepest first
        {
            try
            {
                var info = new DirectoryInfo(folder);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.CreationTime >= cutoff) continue; // deleting files changes a folder's modified time, so its age is when it was made
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // Windows often points %TEMP% and %LOCALAPPDATA%\Temp at the same place: count it once.
    private static IEnumerable<CleanupArea> Distinct(IEnumerable<CleanupArea> areas) =>
        areas.GroupBy(a => System.IO.Path.TrimEndingDirectorySeparator(a.Path), StringComparer.OrdinalIgnoreCase).Select(g => g.First());

    // ---- Text ---------------------------------------------------------------------------------------------

    public static string Describe(IReadOnlyList<CleanupFound> found)
    {
        var total = found.Sum(f => f.Bytes);
        if (total == 0) return "Nothing to clean: there are no temporary files older than a day.";

        var sb = new StringBuilder($"Files older than a day that can go ({SystemInfo.FormatBytes(total)} in all):");
        foreach (var f in found.Where(f => f.Bytes > 0)) // a few empty files aren't worth a line
            sb.Append($"\n• {f.Area.Name}: {SystemInfo.FormatBytes(f.Bytes)} in {f.Files:N0} file(s)");
        return sb.ToString();
    }

    public static string Describe(CleanupResult result) =>
        $"Deleted {result.Deleted:N0} file(s) and freed {SystemInfo.FormatBytes(result.Bytes)}." +
        (result.Failed > 0 ? $" {result.Failed:N0} file(s) were in use and were left alone." : "");

    /// <summary>The areas the user or model named; all of them when none are named.</summary>
    public static IReadOnlyList<CleanupArea> Pick(IEnumerable<string>? keys)
    {
        var all = DefaultAreas();
        var wanted = keys?.Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToHashSet();
        return wanted is { Count: > 0 } ? all.Where(a => wanted.Contains(a.Key)).ToList() : all;
    }
}
