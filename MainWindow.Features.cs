using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.Tools;

namespace WinCompanion;

// Routines, focus mode, the daily briefing, chat export, clipboard history and the screen follow-ups.
public partial class MainWindow
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex BriefingRequest = new(
        @"^\W*(?:please\s+)?(?:(?:give|show|read)\s+me\s+)?(?:(?:my|the|today'?s)\s+)*(?:(?:daily|morning)\s+)?briefing\W*$|^\W*brief\s+me\W*$", Opts);

    private static readonly Regex ExportRequest = new(
        @"^\W*(?:please\s+)?(?:export|save)\s+(?:this\s+|my\s+|the\s+)?(?:chat|conversation|history)(?:\s+(?:as|to)\s+(?:a\s+)?(?:markdown|md|file|markdown file))?\W*$", Opts);

    private static readonly Regex ClearClipboardRequest = new(
        @"^\W*(?:please\s+)?(?:clear|delete|erase)\s+(?:my\s+|the\s+)?clipboard\s+history\W*$", Opts);

    private static readonly Regex SystemRequest = new(
        @"^\W*(?:please\s+)?(?:(?:what(?:'s|\s+is|\s+are)|show(?:\s+me)?|tell\s+me|give\s+me)\s+)?(?:my\s+|the\s+|this\s+)?(?:pc|computer|laptop|system|hardware)\s+(?:specs?|specifications|details|info|information|configuration)\W*$" +
        @"|^\W*(?:system|pc|hardware)\s+(?:specs?|info)\W*$", Opts);

    private RoutineStore _routines = null!;
    private FocusService _focus = null!;
    private ClipboardWatcher? _clipboardWatcher;
    private bool _screenToolUsed;

    /// <summary>Creates the feature services. Called from the constructor, before the tool registry.</summary>
    private AppFeatures CreateFeatures()
    {
        _routines = new RoutineStore();
        _focus = new FocusService(_reminders);
        return new AppFeatures(_routines, _focus);
    }

    /// <summary>Hooks the registry into the tool context and starts the clipboard watcher and the morning briefing.</summary>
    private void StartFeatures()
    {
        _ctx.Tools = _tools;
        _ctx.Executor = new ToolExecutor(_tools, ConfirmAsync, _log);

        _clipboardWatcher = new ClipboardWatcher(() => _appSettings.ClipboardHistory);
        _clipboardWatcher.Start(this);

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () => await ShowBriefingIfDueAsync());
    }

    /// <summary>
    /// Requests the app answers itself, without a model: routines, focus mode, the briefing, chat export and clearing clipboard history.
    /// Returns the reply, or null to carry on (local reminders, then the AI).
    /// </summary>
    private async Task<string?> TryHandleFeatureAsync(string text)
    {
        var input = text.Trim();

        if (_routines.Find(input) is { } routine) return await RoutineTools.Run(_ctx, routine);
        if (_focus.TryHandle(input) is { } focusReply) return focusReply;
        if (BriefingRequest.IsMatch(input)) return await BriefingService.BuildAsync(_reminders, _appSettings);
        if (ExportRequest.IsMatch(input)) return ChatExport.Export(null, _appSettings.AssistantName);
        if (ClearClipboardRequest.IsMatch(input)) return $"Cleared {ClipboardStore.Clear()} item(s) from the clipboard history.";
        if (SystemRequest.IsMatch(input)) return SystemReport();
        if (AboutTessa.IsAboutQuestion(input)) return AboutText();
        if (AboutTessa.IsFeaturesQuestion(input)) return AboutTessa.Features(_appSettings.AssistantName);
        return null;
    }

    private string AboutText() => AboutTessa.About(_appSettings.AssistantName,
        _appSettings.UseLocal ? $"a local model ({_appSettings.LocalModel})" : "Google Gemini");

    private void ClearClipboard_Click(object sender, RoutedEventArgs e) =>
        SettingsNote.Text = $"Cleared {ClipboardStore.Clear()} item(s) from the clipboard history.";

    // With the option on, the first start of each day shows the briefing in the chat and pops a notification.
    private async Task ShowBriefingIfDueAsync()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_appSettings.BriefingAtStart || _appSettings.BriefingLastShown == today) return;

        _appSettings.BriefingLastShown = today;
        _appSettings.Save();
        try
        {
            AddMessage("assistant", await BriefingService.BuildAsync(_reminders, _appSettings));
            ReminderFired?.Invoke($"{_appSettings.AssistantName}: your briefing", "Your daily briefing is ready.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* offline: try again tomorrow */ }
    }

    // While a focus work period runs, other reminders wait until it ends, and she keeps quiet.
    private bool HoldForFocus(Reminder reminder)
    {
        if (FocusService.IsFocusReminder(reminder.Message)) return false;
        if (_focus.WorkEnds(DateTime.Now) is not { } end) return false;
        _reminders.Add(reminder.Message, end.AddSeconds(3));
        return true;
    }

    private bool FocusIsQuiet => _focus.IsWorking(DateTime.Now);

    // After a screen question, offer the obvious next steps as chips above the input.
    private void NoteToolCall(string call)
    {
        if (call.StartsWith("look_at_screen", StringComparison.Ordinal) || call.StartsWith("read_screen_text", StringComparison.Ordinal))
            _screenToolUsed = true;
    }

    private void ShowScreenFollowUps()
    {
        if (!_screenToolUsed) return;
        _screenToolUsed = false;

        Chips.Children.Clear();
        AddChip("Copy the text on screen", "Copy the text on my screen to the clipboard.");
        AddChip("Explain this error", "Look at my screen and explain any error or message you see, and how to fix it.");
        AddChip("Summarize this", "Look at my screen and summarize what is on it.");
    }

    private void AddChip(string label, string prompt)
    {
        var chip = new Button { Content = label, Style = (Style)FindResource("Chip") };
        chip.Click += async (_, _) => await SubmitAsync(label, spoken: false, sendText: prompt);
        Chips.Children.Add(chip);
    }
}
