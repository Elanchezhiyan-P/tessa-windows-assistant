using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

/// <summary>The general-purpose escape hatch: the model writes a PowerShell command and it runs here.</summary>
internal static class PowerShellTool
{
    private const int DefaultTimeoutSeconds = 30, MaxTimeoutSeconds = 300, MaxOutputChars = 6000;

    // Quiet progress bars and force UTF-8 so output comes back readable.
    private const string Preamble =
        "$ProgressPreference='SilentlyContinue'; $OutputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.Encoding]::UTF8; ";

    public static IEnumerable<ITool> All(AppSettings settings)
    {
        yield return new DelegateTool("run_powershell",
            "Run a PowerShell command on this PC and return its output. Use for anything other tools don't cover " +
            "(winget installs, system queries, files, network). Write the exact command. Runs without admin rights unless run_as_admin is true (only when the user enabled it). Optional timeout_seconds (max 300).",
            new JsonObject
            {
                ["command"] = Str("The PowerShell command or script to run."),
                ["timeout_seconds"] = Int("Optional time limit in seconds (default 30, max 300)."),
                ["run_as_admin"] = Bool("Set true only when the command needs administrator rights. Windows shows a UAC prompt.")
            }, ["command"], true,
            args => args["run_as_admin"]?.GetValue<bool>() == true
                ? (settings.PowerShellAdmin
                    ? RunElevatedAsync(args["command"]!.GetValue<string>(), args["timeout_seconds"]?.GetValue<int>())
                    : Task.FromResult("Administrator commands are turned off. Tell the user they can enable them in Settings, PowerShell commands."))
                : RunAsync(args["command"]!.GetValue<string>(), args["timeout_seconds"]?.GetValue<int>()),
            // An administrator command always needs the user's approval, even a read-only one.
            confirmWhen: args => args["run_as_admin"]?.GetValue<bool>() == true
                || SafeCommands.RequiresApproval(args["command"]?.GetValue<string>() ?? "", settings.PowerShellTrusted));
    }

    /// <summary>Runs the command in an elevated PowerShell (Windows shows the UAC prompt) and reads its output back from a temp file.</summary>
    private static async Task<string> RunElevatedAsync(string command, int? timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds));
        var id = Guid.NewGuid().ToString("N");
        var script = Path.Combine(Path.GetTempPath(), $"tessa-{id}.ps1");
        var result = Path.Combine(Path.GetTempPath(), $"tessa-{id}.txt");
        // The elevated process can't be redirected, so the script writes everything (including errors) to a file.
        File.WriteAllText(script, Preamble +
            "try { & { " + command + "\n } *>&1 | Out-String -Width 200 | Set-Content -Encoding UTF8 -LiteralPath '" + result + "'; exit 0 } " +
            "catch { $_ | Out-String | Set-Content -Encoding UTF8 -LiteralPath '" + result + "'; exit 1 }");
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\""
            };
            Process process;
            try { process = Process.Start(psi)!; }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "The user declined the Windows administrator prompt, so the command was not run.";
            }
            using (process)
            {
                using var cts = new CancellationTokenSource(timeout);
                try { await process.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { /* may be denied for an elevated process */ }
                    return $"Timed out after {(int)timeout.TotalSeconds}s. The elevated command may still be running.";
                }
                var output = File.Exists(result) ? File.ReadAllText(result).Trim() : "";
                if (output.Length > MaxOutputChars) output = output[..MaxOutputChars] + "\n...(truncated)";
                if (output.Length == 0) output = "(no output)";
                return process.ExitCode == 0 ? output : $"{output}\n[exit code {process.ExitCode}]";
            }
        }
        finally
        {
            try { File.Delete(script); File.Delete(result); } catch { /* best effort */ }
        }
    }

    private static async Task<string> RunAsync(string command, int? timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Preamble + command })
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);

        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            return $"Timed out after {(int)timeout.TotalSeconds}s and was stopped. Retry with a larger timeout_seconds if it needs longer.";
        }

        var output = (await stdout + await stderr).Trim();
        if (output.Length > MaxOutputChars) output = output[..MaxOutputChars] + "\n...(truncated)";
        if (output.Length == 0) output = "(no output)";
        return process.ExitCode == 0 ? output : $"{output}\n[exit code {process.ExitCode}]";
    }
}
