using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinCompanion.Services;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

// "/clear", "/new", "/export", "/copy" and "/help", with a suggestion list while typing a slash command.
public partial class MainWindow
{
    /// <summary>
    /// Runs a slash command. Returns null when <paramref name="text"/> is not one; otherwise the reply to show
    /// (empty when the command already changed the screen itself).
    /// </summary>
    private async Task<string?> TryHandleSlashAsync(string text)
    {
        if (SlashCommands.Parse(text) is not var (name, args)) return null;

        switch (name)
        {
            case "undo":
                return UndoLog.Shared.Undo();
            case "search":
                return args.Length == 0
                    ? "Type what to look for, for example /search invoice."
                    : ChatSearch.Run(args, _appSettings.AssistantName);
            case "nudge":
                return NudgeCommand(args);
            case "update":
                return await UpdateCheck.CheckAsync();
            case "diagnostics":
                return CopyDiagnostics();
            case "system":
                return SystemReport();
            case "about":
                return AboutText();
            case "features":
                return AboutTessa.Features(_appSettings.AssistantName);
            case "briefing":
                return await BriefingService.BuildAsync(_reminders, _appSettings);
            case "focus":
                return FocusCommand(args);
            case "routine" when args.Length > 0:
                return _routines.Get(args) is { } routine ? await RoutineTools.Run(_ctx, routine) : $"I have no routine called \"{args}\".";
            case "routine" or "routines":
                return RoutineList();
            case "reminders":
                return _reminders.Describe();
            case "clipboard":
                return ClipboardCommand(args);
            case "snip":
                SnipText();
                return "";
            case "ocr":
                if (_ctx.Executor is null) return "Screen text isn't ready yet.";
                var scope = args.Equals("screen", StringComparison.OrdinalIgnoreCase) ? "screen" : "active_window";
                return await _ctx.Executor.RunAsync("read_screen_text", new JsonObject { ["scope"] = scope });
            case "note":
                if (args.Length == 0) return "Type what to save, for example /note buy milk or /note todo: call Sam.";
                var (noteText, todo) = NotesTools.ParseCapture(args);
                return noteText.Length == 0 ? "There is nothing to save." : $"{(todo ? "To-do" : "Note")} saved to {NotesTools.Append(noteText, todo)}.";
            case "model":
                return ModelCommand(args);
            case "voice":
                return VoiceCommand(args);
            case "theme":
                return ThemeCommand(args);
            case "quiet":
                return QuietCommand(args);
            case "health":
                return HealthCommand(args);
            case "memory":
                var remembered = _memory.List();
                return remembered.Count == 0
                    ? "I don't remember anything about you yet. Tell me “remember that …”."
                    : "What I remember (these are sent to the model with each request; /forget removes one):\n" + string.Join('\n', remembered.Select(m => $"• {m.Text}"));
            case "forget":
                if (args.Length == 0) return "Say what to forget, for example /forget project folder, or /forget all.";
                return args.Equals("all", StringComparison.OrdinalIgnoreCase) ? $"Forgot {_memory.Clear()} thing(s)." : _memory.Forget(args);
            case "tour":
                ShowTour();
                return "";
            case "settings":
                if (!_settingsOpen) Settings_Click(this, new RoutedEventArgs());
                else CancelSettings_Click(this, new RoutedEventArgs());
                return "";
            case "clear":
                NewChat_Click(this, new RoutedEventArgs());
                return "";
            case "new":
                var saved = _chat.Entries.Count > 0 ? ChatExport.Export(null, _appSettings.AssistantName) : null;
                NewChat_Click(this, new RoutedEventArgs());
                if (saved is not null) AddMessage("assistant", $"Started a new chat. {saved}");
                return "";
            case "export":
                return ChatExport.Export(args, _appSettings.AssistantName);
            case "copy":
                var last = _chat.Entries.LastOrDefault(e => e.Role == "assistant");
                if (last is null) return "There is no reply to copy yet.";
                try { Clipboard.SetText(last.Text); }
                catch (System.Runtime.InteropServices.COMException) { return "The clipboard is busy; try again."; }
                return "Copied the last reply.";
            case "help":
                return SlashCommands.HelpText();
            default:
                return SlashCommands.Unknown(name);
        }
    }

    private string FocusCommand(string args)
    {
        if (args is "stop" or "off" or "end")
            return _focus.Stop() ? "Focus mode is off." : "Focus mode wasn't on.";
        if (args.Equals("pomodoro", StringComparison.OrdinalIgnoreCase)) return _focus.Start(25, 5, 4);
        if (args.Length == 0) return _focus.Start(25, 5, 1);
        return int.TryParse(args.Replace("min", "").Replace("m", "").Trim(), out var minutes) && minutes > 0
            ? _focus.Start(minutes, 5, 1)
            : "Use /focus, /focus 45, /focus pomodoro or /focus stop.";
    }

    private NudgeService? _nudges;

    private string NudgeCommand(string args)
    {
        _nudges ??= new NudgeService(_reminders);
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return _nudges.Status();
        if (parts[0].Equals("off", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            return _nudges.Stop(parts.Length > 1 ? parts[1] : null);

        if (NudgeService.Find(parts[0]) is not { } kind) return $"I don't know a nudge called \"{parts[0]}\". Try {NudgeService.KeyList()}.";
        if (parts.Length == 1) return _nudges.Start(kind);
        return int.TryParse(parts[1].Replace("min", "").Replace("m", "").Trim(), out var minutes) && minutes > 0
            ? _nudges.Start(kind, minutes)
            : "Give the minutes as a number, for example /nudge water 45.";
    }

    /// <summary>Copies the bug-report details to the clipboard and says how much was copied.</summary>
    private string CopyDiagnostics()
    {
        var text = Diagnostics.Build(Specs, _appSettings, Diagnostics.CrashTail());
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { return "The clipboard is busy; try again."; }
        return $"Copied {text.Split('\n').Length} lines of diagnostics to the clipboard. They have no chat, keys or notes, and your user and PC names are hidden. Read them before you paste them into a bug report.";
    }

    private string RoutineList()
    {
        var all = _routines.List();
        if (all.Count == 0) return "No routines saved yet. Ask me to make one, for example \"make a morning routine that opens Outlook\".";
        return "Routines (run one with /routine <name>):\n" + string.Join('\n', all.Select(r =>
            $"• {r.Name}{(r.Phrase is null ? "" : $" (say \"{r.Phrase}\")")}: {string.Join(", then ", r.Steps.Select(s => s.Tool))}"));
    }

    private string ClipboardCommand(string args)
    {
        if (args.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return $"Cleared {ClipboardStore.Clear()} item(s) from the clipboard history.";

        var items = ClipboardStore.Find(args, null, 10);
        if (items.Count == 0)
            return _appSettings.ClipboardHistory ? "Nothing in the clipboard history matches." : "Clipboard history is off. Turn it on in /settings.";
        return "Recently copied:\n" + string.Join('\n', items.Select(i =>
        {
            var line = i.Text.ReplaceLineEndings(" ");
            return $"• {i.Time:d MMM HH:mm}: {(line.Length > 120 ? line[..120] + "…" : line)}";
        }));
    }

    private string ModelCommand(string args)
    {
        if (args.Length == 0)
            return _appSettings.UseLocal
                ? $"Using the local model {_appSettings.LocalModel}. Switch with /model gemini."
                : "Using Gemini. Switch with /model local.";

        var local = args.Equals("local", StringComparison.OrdinalIgnoreCase);
        if (!local && !args.Equals("gemini", StringComparison.OrdinalIgnoreCase)) return "Use /model gemini or /model local.";
        if (local == _appSettings.UseLocal) return "Already using that model.";
        if (!local && string.IsNullOrEmpty(_settings.LoadApiKey())) return "Add a Gemini API key in /settings first.";

        _appSettings.Provider = local ? "local" : "gemini";
        _appSettings.Save();
        _llm = null; // rebuilt with the new provider on the next request
        _ctx.Llm = null;
        RefreshModelLabel();
        return local ? $"Switched to the local model {_appSettings.LocalModel}." : "Switched to Gemini.";
    }

    private string VoiceCommand(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

        if (first == "list") return VoiceList();
        if (first == "speed") return VoiceSpeedCommand(parts.Length > 1 ? parts[1] : "");
        if (first.Length > 0 && first is not ("on" or "off")) return VoiceByName(args);

        bool? muted = first switch
        {
            "on" => false,
            "off" => true,
            _ => null
        };
        if (muted is null)
            return (_appSettings.VoiceMuted ? "Her voice is off." : "Her voice is on.") +
                   $" Voice: {(_appSettings.VoiceName.Length > 0 ? _appSettings.VoiceName : "automatic")}, speed {SpeedText(_appSettings.VoiceRate)}. " +
                   "Use /voice on, off, list, speed 2, or part of a voice name.";

        _appSettings.VoiceMuted = muted.Value;
        _appSettings.Save();
        if (muted.Value) _speaker.SpeakAsyncCancelAll();
        UpdateMuteButton();
        return muted.Value ? "Voice muted." : "Voice on.";
    }

    private IEnumerable<string> InstalledVoiceNames()
    {
        try { return _speaker.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return []; }
    }

    private string VoiceList()
    {
        var names = InstalledVoiceNames().ToList();
        return names.Count == 0
            ? "No Windows voices are installed."
            : "Voices on this PC (pick one with /voice <part of the name>):\n" + string.Join('\n', names.Select(n => $"• {n}"));
    }

    private string VoiceByName(string wanted)
    {
        var matches = InstalledVoiceNames().Where(n => n.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return $"I have no voice matching \"{wanted}\". Try /voice list.";
        if (matches.Count > 1 && !matches.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            return "That matches more than one voice: " + string.Join(", ", matches) + ". Be more specific.";

        _appSettings.VoiceName = matches.FirstOrDefault(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? matches[0];
        _appSettings.Save();
        ApplyVoice();
        _speaker.SpeakAsyncCancelAll();
        if (!_appSettings.VoiceMuted) _speaker.SpeakAsync("Hello, this is my new voice.");
        return $"Now using {_appSettings.VoiceName}.";
    }

    private string VoiceSpeedCommand(string text)
    {
        if (!int.TryParse(text, out var rate)) return $"Speed is {SpeedText(_appSettings.VoiceRate)}. Use /voice speed with a number from -6 (slow) to 6 (fast), or 0 for normal.";
        _appSettings.VoiceRate = Math.Clamp(rate, -6, 6);
        _appSettings.Save();
        ApplyVoice();
        return $"Speed set to {SpeedText(_appSettings.VoiceRate)}.";
    }

    private string ThemeCommand(string args)
    {
        if (args.Length == 0)
            return $"Theme: {ThemeCatalog.Find(_appSettings.Theme).Name}{(_appSettings.AccentColor.Length > 0 ? $" with accent {_appSettings.AccentColor}" : "")}.\n" +
                   $"Themes: {string.Join(", ", ThemeCatalog.All.Select(t => t.Key))}. Use /theme <name>, /theme #FF8800 for your own accent, or /theme reset.";

        if (args.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            _appSettings.Theme = "midnight";
            _appSettings.AccentColor = "";
        }
        else if (ThemeCatalog.TryParseAccent(args, out var accent))
        {
            _appSettings.AccentColor = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        }
        else if (ThemeCatalog.All.FirstOrDefault(t => t.Key.Equals(args, StringComparison.OrdinalIgnoreCase)) is { } theme)
        {
            _appSettings.Theme = theme.Key;
            _appSettings.AccentColor = "";
        }
        else return $"I don't know a theme called \"{args}\". Try {string.Join(", ", ThemeCatalog.All.Select(t => t.Key))}.";

        _appSettings.Save();
        ApplyTheme(_appSettings.Theme, _appSettings.AccentColor);
        return $"Theme: {ThemeCatalog.Find(_appSettings.Theme).Name}{(_appSettings.AccentColor.Length > 0 ? $" with accent {_appSettings.AccentColor}" : "")}.";
    }

    private string QuietCommand(string args)
    {
        if (args.Length == 0) return QuietHours.Describe(_appSettings);

        if (args.Equals("on", StringComparison.OrdinalIgnoreCase)) _appSettings.QuietHours = true;
        else if (args.Equals("off", StringComparison.OrdinalIgnoreCase)) _appSettings.QuietHours = false;
        else if (QuietHours.TryParseRange(args, out var start, out var end))
        {
            _appSettings.QuietStart = QuietHours.Format(start);
            _appSettings.QuietEnd = QuietHours.Format(end);
            _appSettings.QuietHours = true;
        }
        else return "Use /quiet on, /quiet off, or a range like /quiet 22:00-07:00.";

        _appSettings.Save();
        return QuietHours.Describe(_appSettings);
    }

    private string HealthCommand(string args)
    {
        if (args.Equals("on", StringComparison.OrdinalIgnoreCase) || args.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _appSettings.HealthAlerts = args.Equals("on", StringComparison.OrdinalIgnoreCase);
            _appSettings.Save();
        }
        return HealthReport();
    }

    // ---- Suggestions ---------------------------------------------------------------------------------

    private void UpdateSlashSuggestions()
    {
        var matches = SlashCommands.Suggest(Input.Text);
        if (matches.Count == 0)
        {
            SlashPopup.IsOpen = false;
            return;
        }

        SlashList.ItemsSource = matches.Select(c => $"{c.Usage}   {c.Description}").ToList();
        SlashList.Tag = matches;
        SlashList.SelectedIndex = 0;
        SlashPopup.IsOpen = true;
    }

    private SlashCommand? SelectedSlashCommand =>
        SlashPopup.IsOpen && SlashList.Tag is IReadOnlyList<SlashCommand> list && SlashList.SelectedIndex >= 0
            ? list[SlashList.SelectedIndex]
            : null;

    /// <summary>Handles Up, Down, Tab, Enter and Escape while the suggestion list is open. Returns true when the key was used.</summary>
    private bool HandleSlashKey(Key key)
    {
        if (!SlashPopup.IsOpen) return false;

        switch (key)
        {
            case Key.Down or Key.Up:
                var count = SlashList.Items.Count;
                SlashList.SelectedIndex = (SlashList.SelectedIndex + (key == Key.Down ? 1 : count - 1)) % count;
                return true;
            case Key.Tab:
                CompleteSlash(run: false);
                return true;
            case Key.Enter when SelectedSlashCommand is { } selected && SlashCommands.Parse(Input.Text)?.Name != selected.Name:
                CompleteSlash(run: true);
                return true;
            case Key.Escape:
                SlashPopup.IsOpen = false;
                return true;
            default:
                return false;
        }
    }

    private void CompleteSlash(bool run)
    {
        if (SelectedSlashCommand is not { } command) return;
        SlashPopup.IsOpen = false;
        run &= !command.NeedsArgs;
        Input.Text = "/" + command.Name + (command.NeedsArgs ? " " : "");
        Input.CaretIndex = Input.Text.Length;
        if (run) Send_Click(this, new RoutedEventArgs());
    }

    private void SlashList_Click(object sender, MouseButtonEventArgs e)
    {
        if (SlashList.SelectedIndex >= 0) CompleteSlash(run: true);
    }
}
