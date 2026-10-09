using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using static WinCompanion.Tools.NativeMethods;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

internal static class WindowTools
{
    private sealed record WinInfo(IntPtr Handle, string Title, string Process);

    public static IEnumerable<ITool> All()
    {
        yield return new DelegateTool("list_windows", "List the open application windows (process and title).", null, [], false,
            _ =>
            {
                var windows = ListWindows();
                return Task.FromResult(windows.Count == 0
                    ? "No windows found."
                    : string.Join('\n', windows.Select(w => $"{w.Process}: {w.Title}")));
            });

        yield return new DelegateTool("window_action",
            "Control a window found by (part of) its title or its app name: minimize, maximize, restore, bring to front, or snap to the left/right half of the screen.",
            new JsonObject
            {
                ["target"] = Str("Part of the window title or the app/process name, e.g. 'chrome' or 'notepad'."),
                ["action"] = Choice("What to do.", "minimize", "maximize", "restore", "focus", "snap_left", "snap_right")
            }, ["target", "action"], false,
            args =>
            {
                var target = args["target"]!.GetValue<string>();
                var action = args["action"]!.GetValue<string>();
                var window = FindWindow(target);
                if (window is null) return Task.FromResult($"No window matching '{target}'.");

                switch (action)
                {
                    case "minimize": ShowWindow(window.Handle, SwMinimize); break;
                    case "maximize": ShowWindow(window.Handle, SwMaximize); break;
                    case "restore": ShowWindow(window.Handle, SwRestore); break;
                    case "focus": BringToFront(window.Handle); break;
                    case "snap_left": Snap(window.Handle, left: true); break;
                    case "snap_right": Snap(window.Handle, left: false); break;
                }
                return Task.FromResult($"{action} done for {window.Process}: {window.Title}");
            });

        yield return new DelegateTool("close_window",
            "Ask the window of an app to close (as if the user clicked X; the app may prompt to save). Matches by title or app name.",
            new JsonObject { ["target"] = Str("Part of the window title or the app/process name.") }, ["target"], true,
            args =>
            {
                var target = args["target"]!.GetValue<string>();
                var window = FindWindow(target);
                if (window is null) return Task.FromResult($"No window matching '{target}'.");
                PostMessage(window.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return Task.FromResult($"Asked {window.Process}: {window.Title} to close.");
            });
    }

    private static List<WinInfo> ListWindows()
    {
        var list = new List<WinInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var length = GetWindowTextLength(hwnd);
            if (length == 0) return true;
            if ((GetWindowLong(hwnd, GwlExStyle) & WsExToolWindow) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true; // hidden UWP shells

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);
            list.Add(new WinInfo(hwnd, title.ToString(), ProcessName(pid)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException) { return "?"; }
    }

    private static WinInfo? FindWindow(string query)
    {
        var all = ListWindows();
        return all.FirstOrDefault(w => w.Process.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(w => w.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(w => w.Process.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static void BringToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        PressKey(VkMenu); // a synthetic Alt press lets SetForegroundWindow succeed from a background process
        SetForegroundWindow(hwnd);
    }

    private static void Snap(IntPtr hwnd, bool left)
    {
        ShowWindow(hwnd, SwRestore);
        var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var half = area.Width / 2;
        SetWindowPos(hwnd, IntPtr.Zero, left ? area.Left : area.Left + half, area.Top, half, area.Height,
            SwpNoZOrder | SwpShowWindow);
    }
}
