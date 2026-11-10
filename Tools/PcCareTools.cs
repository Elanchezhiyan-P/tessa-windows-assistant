using System.Text.Json.Nodes;
using WinCompanion.Services;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

/// <summary>Looking after the PC: what starts with Windows, junk files, and why it feels slow. Anything that changes something asks first.</summary>
internal static class PcCareTools
{
    public static IEnumerable<ITool> All()
    {
        yield return new DelegateTool("list_startup_apps",
            "List the programs that start with Windows and whether each is on or off.",
            null, [], false, _ => Task.FromResult(StartupApps.Describe(new StartupApps().List())));

        yield return new DelegateTool("set_startup_app",
            "Turn a program on or off in Windows' start-up list, so it does or doesn't start when the user signs in. Nothing is uninstalled and it can be switched back on.",
            new JsonObject
            {
                ["name"] = Str("The program's name as listed by list_startup_apps, or part of it."),
                ["enabled"] = Bool("True to let it start with Windows, false to stop it.")
            }, ["name", "enabled"], true,
            args => Task.FromResult(new StartupApps().SetEnabled(
                args["name"]!.GetValue<string>(), args["enabled"]?.GetValue<bool>() ?? false)),
            describe: args =>
            {
                var enabled = args["enabled"]?.GetValue<bool>() ?? false;
                return $"{(enabled ? "Let" : "Stop")} \"{args["name"]?.GetValue<string>()}\" {(enabled ? "start" : "from starting")} with Windows.\nNothing is removed, and you can switch it back.";
            });

        yield return new DelegateTool("scan_temp_files",
            "Report how much junk (temporary files, internet cache, crash dumps, error reports older than a day) could be cleaned. Changes nothing.",
            null, [], false, async _ => TempCleanup.Describe(await Task.Run(() => TempCleanup.Scan())));

        yield return new DelegateTool("clean_temp_files",
            "Permanently delete temporary files, internet cache, crash dumps and error reports that are over a day old. Only those folders are touched, and the files can't be recovered, so the user is always asked first. " +
            "Run scan_temp_files first to see how much there is.",
            new JsonObject
            {
                ["areas"] = Str("Optional, comma separated: temp, cache, dumps, reports. Leave out for all of them.")
            }, [], true,
            async args =>
            {
                var areas = TempCleanup.Pick(Keys(args));
                return TempCleanup.Describe(await Task.Run(() => TempCleanup.Clean(areas)));
            },
            describe: args =>
            {
                var found = TempCleanup.Scan(TempCleanup.Pick(Keys(args)));
                return found.Sum(f => f.Bytes) == 0
                    ? "There is nothing to clean (no files older than a day)."
                    : "Permanently delete these files. They can't be recovered, and files from the last day are left alone.\n" + TempCleanup.Describe(found);
            });

        yield return new DelegateTool("why_is_my_pc_slow",
            "Check why the PC feels slow: processor and memory use, the busiest programs, nearly-full drives, start-up programs, uptime and junk files. Returns likely causes, each with a fix. Changes nothing.",
            null, [], false, _ => DiagnoseAsync());
    }

    private static IEnumerable<string>? Keys(JsonObject args) =>
        args["areas"]?.GetValue<string>()?.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);

    public static async Task<string> DiagnoseAsync()
    {
        var (cpu, ram, topCpu, topRam) = await SystemTools.SampleAsync();
        var inputs = await Task.Run(() => new SlowdownInputs(
            cpu, ram, topCpu, topRam,
            SystemInfo.ReadDrives(),
            new StartupApps().List().Count(i => i.Enabled),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            TempCleanup.Scan().Sum(f => f.Bytes)));
        return SlowdownAnalysis.Describe(inputs, SlowdownAnalysis.Analyze(inputs));
    }
}
